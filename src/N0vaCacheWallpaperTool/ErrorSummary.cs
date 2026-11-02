namespace N0vaCacheWallpaperTool;

internal static class ErrorSummary
{
    private const int MaximumLength = 120;

    public static string Format(string context, string reason)
    {
        string brief = reason
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim() ?? "未知错误";
        if (brief.Length > MaximumLength)
        {
            brief = brief[..MaximumLength] + "…";
        }
        return context + "：" + brief;
    }
}
