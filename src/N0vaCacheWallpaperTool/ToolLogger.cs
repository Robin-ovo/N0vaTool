using System.Text;

namespace N0vaCacheWallpaperTool;

internal static class ToolLogger
{
    private static readonly object Gate = new();

    public static string CurrentFile =>
        Path.Combine(
            ToolPaths.LogDirectory,
            "N0vaCacheWallpaperTool-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Warning(string message)
    {
        Write("WARN", message);
    }

    public static void Error(string context, Exception exception)
    {
        Write("ERROR", context + Environment.NewLine + exception);
    }

    public static int DeleteLogs()
    {
        if (!Directory.Exists(ToolPaths.LogDirectory)) return 0;

        int deleted = 0;
        foreach (string file in Directory.EnumerateFiles(
                     ToolPaths.LogDirectory,
                     "*.log",
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
            deleted++;
        }
        return deleted;
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ToolPaths.LogDirectory);
                File.AppendAllText(
                    CurrentFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never block wallpaper operations.
        }
    }
}
