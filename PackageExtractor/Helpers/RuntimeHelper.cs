using System.Reflection;
using Sandbox;

namespace PackageExtractor.Helpers;

public static class RuntimeHelper
{
    public static void BootstrapRuntimeAssemblies(string searchPath)
    {
        try
        {
            var runtimeDirs = GetManagedSearchRoots(searchPath).ToList();
            if (runtimeDirs.Count == 0)
            {
                Log.Warning("Managed runtime directories were not found in the specified path. Fallback .cll compilation might fail.");
                return;
            }

            var runtimeDlls = runtimeDirs
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                .Where(x => Path.GetFileName(x).StartsWith("Sandbox.", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(x).StartsWith("Facepunch.", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(x).Equals("Microsoft.AspNetCore.Components.dll", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var loaded = 0;
            foreach (var dll in runtimeDlls)
            {
                try
                {
                    Assembly.LoadFrom(dll);
                    loaded++;
                }
                catch
                {
                    // ignore
                }
            }

            loaded += BootstrapDotNetRuntimeAssemblies();
            PrimeFrameworkReferences();

            Log.Info($"Loaded {loaded} runtime assemblies from {runtimeDlls.Count} managed dlls + dotnet.");
        }
        catch (Exception ex)
        {
            Log.Warning($"Error preloading runtime assemblies: {ex.Message}");
        }
    }

    public static IReadOnlyCollection<string> GetManagedSearchRoots(string searchPath)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(searchPath))
            return roots;
        
        var managedDirs = Directory.EnumerateDirectories(searchPath, "managed", SearchOption.AllDirectories)
            .Where(dir => string.Equals(Path.GetFileName(dir), "managed", StringComparison.OrdinalIgnoreCase));

        foreach (var dir in managedDirs)
        {
            roots.Add(Path.GetFullPath(dir));
        }

        var engineDll = Directory.EnumerateFiles(searchPath, "Sandbox.Engine.dll", SearchOption.AllDirectories).FirstOrDefault();
        var runtimeDir = string.IsNullOrWhiteSpace(engineDll) ? null : Path.GetDirectoryName(engineDll);
        if (!string.IsNullOrWhiteSpace(runtimeDir) && Directory.Exists(runtimeDir))
        {
            roots.Add(Path.GetFullPath(runtimeDir));
        }

        return roots;
    }

    public static IReadOnlyCollection<string> GetDotNetReferenceSearchRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dotNetRoot = GetDotNetRoot();

        if (string.IsNullOrWhiteSpace(dotNetRoot) || !Directory.Exists(dotNetRoot))
            return roots;

        AddLatestReferencePackRoots(roots, Path.Combine(dotNetRoot, "packs", "Microsoft.NETCore.App.Ref"));
        return roots;
    }
    
    private static int BootstrapDotNetRuntimeAssemblies()
    {
        var loaded = 0;

        var coreNames = new[]
        {
            "System.Runtime",
            "System.Private.CoreLib",
            "netstandard",
            "System.Collections",
            "System.Collections.Specialized",
            "System.Collections.Immutable",
            "System.Linq",
            "System.Console",
            "System.Text.Json",
            "System.Text.RegularExpressions",
            "System.ComponentModel.Primitives",
            "System.Runtime.Extensions",
            "System.Runtime.InteropServices",
            "System.ObjectModel",
            "System.Text.Encoding.Extensions",
            "System.Globalization",
            "System.Memory",
            "System.Net.Http",
            "System.Net.Primitives",
            "System.Numerics.Vectors",
            "System.Private.Uri"
        };

        foreach (var name in coreNames)
        {
            try
            {
                Assembly.Load(new AssemblyName(name));
                loaded++;
            }
            catch
            {
                // ignore
            }
        }

        return loaded;
    }

    private static void AddLatestReferencePackRoots(HashSet<string> roots, string packBaseDirectory)
    {
        if (!Directory.Exists(packBaseDirectory))
            return;

        var latestPackDirectory = Directory.EnumerateDirectories(packBaseDirectory)
            .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
            .OrderByDescending(x => x.Version)
            .ThenByDescending(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Path;

        if (string.IsNullOrWhiteSpace(latestPackDirectory))
            return;

        var refBaseDirectory = Path.Combine(latestPackDirectory, "ref");
        if (!Directory.Exists(refBaseDirectory))
            return;

        foreach (var tfmDirectory in Directory.EnumerateDirectories(refBaseDirectory))
        {
            roots.Add(Path.GetFullPath(tfmDirectory));
        }
    }

    private static string? GetDotNetRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
            GetDotNetRootFromAssembly(typeof(object).Assembly.Location)
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
    }

    private static string? GetDotNetRootFromAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return null;

        var directoryPath = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
            return null;

        var directory = new DirectoryInfo(directoryPath);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "packs")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static Version ParseVersion(string? value)
    {
        return Version.TryParse(value, out var version)
            ? version
            : new Version(0, 0);
    }

    private static void PrimeFrameworkReferences()
    {
        try
        {
            var frameworkReferencesType = typeof(CodeArchive).Assembly.GetType("Sandbox.FrameworkReferences");
            var allProperty = frameworkReferencesType?.GetProperty("All", BindingFlags.Static | BindingFlags.Public);
            var allReferences = allProperty?.GetValue(null);
            if (allReferences is not System.Collections.IDictionary dictionary)
                return;

            foreach (var root in GetDotNetReferenceSearchRoots())
            {
                foreach (var dllPath in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(dllPath);
                    if (string.IsNullOrWhiteSpace(fileName) || dictionary.Contains(fileName))
                        continue;

                    try
                    {
                        dictionary[fileName] = Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(dllPath);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Error priming framework references: {ex.Message}");
        }
    }
}
