using System.Reflection;
using Microsoft.CodeAnalysis;
using Sandbox;

namespace PackageExtractor.Helpers;

public sealed class ExtractorReferenceProvider : ICompileReferenceProvider
{
    private readonly Dictionary<string, PortableExecutableReference> _references = new(StringComparer.OrdinalIgnoreCase);

    public ExtractorReferenceProvider(IEnumerable<string> searchRoots)
    {
        LoadFromSearchRoots(searchRoots);
        LoadFromAppDomain();
    }

    public PortableExecutableReference? Lookup(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return _references.GetValueOrDefault(reference);
    }

    private void LoadFromAppDomain()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location))
                    continue;

                var assemblyName = assembly.GetName().Name;
                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    AddReference(assemblyName, assembly.Location);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private void LoadFromSearchRoots(IEnumerable<string> searchRoots)
    {
        foreach (var root in searchRoots
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var dllPath in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    var assemblyName = AssemblyName.GetAssemblyName(dllPath).Name;
                    if (!string.IsNullOrWhiteSpace(assemblyName))
                    {
                        AddReference(assemblyName, dllPath);
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private void AddReference(string assemblyName, string dllPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || string.IsNullOrWhiteSpace(dllPath))
            return;

        if (_references.TryGetValue(assemblyName, out var existingReference))
        {
            var existingPath = existingReference.FilePath;
            if (!ShouldReplaceReference(existingPath, dllPath))
                return;
        }

        _references[assemblyName] = MetadataReference.CreateFromFile(dllPath);
    }

    private static bool ShouldReplaceReference(string? existingPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(existingPath))
            return true;

        var existingIsReferenceAssembly = IsReferenceAssemblyPath(existingPath);
        var candidateIsReferenceAssembly = IsReferenceAssemblyPath(candidatePath);
        
        if (candidateIsReferenceAssembly && !existingIsReferenceAssembly)
            return false;

        if (existingIsReferenceAssembly && !candidateIsReferenceAssembly)
            return true;

        return false;
    }

    private static bool IsReferenceAssemblyPath(string path)
    {
        return path.IndexOf("\\packs\\", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("/packs/", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("\\ref\\", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("/ref/", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
