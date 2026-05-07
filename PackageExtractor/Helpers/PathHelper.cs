namespace PackageExtractor.Helpers;

public static class PathHelper
{
    public static string SanitizePath(string path)
    {
        var safe = path.Replace("..", "");
        
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (c == '/' || c == '\\') continue;
            safe = safe.Replace(c.ToString(), "");
        }

        return safe.TrimStart('/', '\\');
    }
}
