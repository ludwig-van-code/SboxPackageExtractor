using PackageExtractor.Helpers;
using Sandbox;

namespace PackageExtractor;

public class Extractor(string baseOutputDir)
{
    public async Task ExecuteAsync(string searchPath)
    {
        Log.Info($"Searching for game cll in: {searchPath}");

        RuntimeHelper.BootstrapRuntimeAssemblies(searchPath);

        var binPath = Path.Combine(searchPath, "download", "assets", "_bin");
        List<string> allFiles;

        if (Directory.Exists(binPath))
        {
            Log.Info($"Found assets bin directory: {binPath}");
            allFiles = Directory.EnumerateFiles(binPath, "*.cll", SearchOption.AllDirectories).ToList();
        }
        else
        {
            Log.Warning($"Assets bin directory not found at: {binPath}, falling back to recursive search...");
            allFiles = Directory.EnumerateFiles(searchPath, "*.cll", SearchOption.AllDirectories).ToList();
        }

        if (allFiles.Count == 0)
        {
            Log.Info("No game cll found.");
            return;
        }

        var files = allFiles
            .GroupBy(f => GetPackageGroupKey(f))
            .Select(g => g.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First())
            .ToList();

        Log.Info($"Found {allFiles.Count} game cll ({files.Count} unique packages). Starting parallel processing...");

        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        await Parallel.ForEachAsync(files, options, async (file, _) =>
        {
            await ProcessArchiveAsync(file);
        });

        var referenceSearchRoots = RuntimeHelper.GetManagedSearchRoots(searchPath);
        await CompileDllBatchAsync(files, referenceSearchRoots);

        Console.WriteLine();
        Log.Success("All game cll processed successfully.");
    }

    private static string GetPackageGroupKey(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var dotIndex = fileName.LastIndexOf('.');
        if (dotIndex > 0)
        {
            return fileName[..dotIndex];
        }
        return fileName;
    }

    private async Task ProcessArchiveAsync(string filePath)
    {
        try
        {
            var fallbackArchiveName = Path.GetFileNameWithoutExtension(filePath);
            var data = await File.ReadAllBytesAsync(filePath);
            var archive = new CodeArchive(data);
            var archiveName = GetOutputPackageName(archive, fallbackArchiveName);
            var packageOutputDir = Path.Combine(baseOutputDir, archiveName);

            var tasks = new List<Task>();

            foreach (var tree in archive.SyntaxTrees)
            {
                tasks.Add(SaveEntryAsync(packageOutputDir, tree.FilePath, (await tree.GetTextAsync()).ToString()));
            }

            foreach (var additional in archive.AdditionalFiles)
            {
                tasks.Add(SaveEntryAsync(packageOutputDir, additional.LocalPath, additional.Text));
            }

            await Task.WhenAll(tasks);

            Log.Success($"Extracted: {archiveName} ({tasks.Count} files)");
        }
        catch (Exception ex)
        {
            Log.Error($"Error processing {Path.GetFileName(filePath)}: {ex.Message}");
        }
    }

    private async Task SaveEntryAsync(string packageDir, string internalPath, string content)
    {
        if (string.IsNullOrWhiteSpace(internalPath)) return;

        var safePath = PathHelper.SanitizePath(internalPath);
        var fullPath = Path.GetFullPath(Path.Combine(packageDir, safePath));

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content);
    }

    private async Task CompileDllBatchAsync(List<string> cllFiles, IReadOnlyCollection<string> referenceSearchRoots)
    {
        try
        {
            var pending = new List<(string filePath, string outputDir, string archiveName, CodeArchive archive)>();
            var dotNetReferenceRoots = RuntimeHelper.GetDotNetReferenceSearchRoots();

            foreach (var filePath in cllFiles)
            {
                var fallbackArchiveName = Path.GetFileNameWithoutExtension(filePath);
                var bytes = await File.ReadAllBytesAsync(filePath);
                var archive = new CodeArchive(bytes);
                var archiveName = GetOutputPackageName(archive, fallbackArchiveName);
                var packageOutputDir = Path.Combine(baseOutputDir, archiveName);
                var dllDir = Path.Combine(packageOutputDir, "dll");

                var hasDll = Directory.Exists(dllDir) && Directory.EnumerateFiles(dllDir, "*.dll", SearchOption.TopDirectoryOnly).Any();
                if (hasDll)
                    continue;

                pending.Add((filePath, packageOutputDir, archiveName, archive));
            }

            if (pending.Count == 0)
                return;

            using var group = new CompileGroup("SboxPackageExtractorBatch");
            group.AccessControl = null;
            group.ReferenceProvider = new ExtractorReferenceProvider(GetReferenceSearchRoots(referenceSearchRoots).Concat(dotNetReferenceRoots));

            var outputDirByCompiler = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in pending)
            {
                var compiler = group.GetOrCreateCompiler(item.archive.CompilerName);
                compiler.UpdateFromArchive(item.archive);

                outputDirByCompiler[item.archive.CompilerName] = item.outputDir;
            }

            await group.BuildAsync();

            if (!group.BuildResult.Success)
            {
                var diagnostics = group.BuildResult.BuildDiagnosticsString();
                Log.Warning($"Batch compilation of .cll failed with errors: {diagnostics}");
                return;
            }

            foreach (var assembly in group.BuildResult.Output)
            {
                if (!outputDirByCompiler.TryGetValue(assembly.Compiler.Name, out var outputDir))
                    continue;

                var dllDir = Path.Combine(outputDir, "dll");
                Directory.CreateDirectory(dllDir);

                var dllPath = Path.Combine(dllDir, $"{assembly.Compiler.AssemblyName}.dll");
                await File.WriteAllBytesAsync(dllPath, assembly.AssemblyData);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Error during batch compilation of .cll: {ex.Message}");
        }
    }

    private static string GetOutputPackageName(CodeArchive archive, string fallbackArchiveName)
    {
        var compilerName = archive.CompilerName?.Trim();

        if (string.IsNullOrWhiteSpace(compilerName))
            return fallbackArchiveName;

        return $"{compilerName}";
    }

    private static IEnumerable<string> GetReferenceSearchRoots(IEnumerable<string> managedRoots)
    {
        var roots = new HashSet<string>(managedRoots, StringComparer.OrdinalIgnoreCase)
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.CurrentDirectory
        };

        return roots;
    }

}
