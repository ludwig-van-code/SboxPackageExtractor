using PackageExtractor.Helpers;

namespace PackageExtractor;

public static class Program
{
    private const string DefaultOutputDirectory = "extracted";

    public static async Task Main(string[] args)
    {
        Console.Title = "Sbox Package Extractor";
        
        var searchPath = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
        var rootOutputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultOutputDirectory);

        if (!Directory.Exists(searchPath))
        {
            Log.Error($"Path not found: {searchPath}");
            return;
        }

        try
        {
            var extractor = new Extractor(rootOutputFolder);
            await extractor.ExecuteAsync(searchPath);
        }
        finally
        {
            Console.ResetColor();
        }
    }
}