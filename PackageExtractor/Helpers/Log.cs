namespace PackageExtractor.Helpers;

public static class Log
{
    public static void Info(string message)
    {
        Console.WriteLine($"[*] {message}");
    }

    public static void Success(string message)
    {
        WriteColor($"[+] {message}", ConsoleColor.Green);
    }

    public static void Warning(string message)
    {
        WriteColor($"[!] {message}", ConsoleColor.Yellow);
    }

    public static void Error(string message)
    {
        WriteColor($"[!!!] {message}", ConsoleColor.Red);
    }

    private static void WriteColor(string message, ConsoleColor color)
    {
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = oldColor;
    }
}
