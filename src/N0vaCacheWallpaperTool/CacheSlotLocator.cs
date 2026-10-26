using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace N0vaCacheWallpaperTool;

internal static partial class CacheSlotLocator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static CacheSlotInfo? Locate(string hostDirectory, long minimumLength)
    {
        string configFile = Path.Combine(hostDirectory, "config.ini");
        string gameDirectory = Path.Combine(hostDirectory, "N0vaDesktopCache", "game");
        if (!File.Exists(configFile) || !Directory.Exists(gameDirectory))
        {
            return null;
        }

        string resourceId = ReadSelectedResourceId(configFile);

        CacheSlotInfo? known = LocateFromActiveManifest(
            resourceId,
            gameDirectory,
            minimumLength);
        if (known is not null) return known;

        CacheSlotInfo? logged = LocateFromLogs(
            hostDirectory,
            resourceId,
            gameDirectory,
            minimumLength);
        CacheSlotInfo? recent = LocateFromRecentCache(
            configFile,
            gameDirectory,
            resourceId,
            minimumLength);

        if (recent is not null &&
            (logged is null ||
             (!string.Equals(
                  Path.GetFullPath(recent.FilePath),
                  Path.GetFullPath(logged.FilePath),
                  StringComparison.OrdinalIgnoreCase) &&
              Math.Abs(
                  (recent.LastWriteTimeUtc - File.GetLastWriteTimeUtc(configFile))
                  .TotalSeconds) <= 90)))
        {
            return recent;
        }
        return logged ?? recent;
    }

    public static string ReadSelectedResourceId(string configFile)
    {
        Match match = SelectedResourceRegex().Match(File.ReadAllText(configFile));
        if (!match.Success)
        {
            throw new InvalidDataException("N0va 配置中缺少 cur_sel_res_id，无法定位当前壁纸缓存。");
        }
        return match.Groups["id"].Value;
    }

    private static CacheSlotInfo? LocateFromActiveManifest(
        string resourceId,
        string gameDirectory,
        long minimumLength)
    {
        if (!File.Exists(ToolPaths.ActiveSlotManifest)) return null;

        try
        {
            ActiveSlotManifest? manifest = JsonSerializer.Deserialize<ActiveSlotManifest>(
                File.ReadAllText(ToolPaths.ActiveSlotManifest),
                JsonOptions);
            if (manifest is null ||
                !string.Equals(manifest.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(manifest.SlotPath))
            {
                return null;
            }

            FileInfo file = new(manifest.SlotPath);
            if (file.Length != manifest.SlotLength ||
                !string.Equals(HashFile(file.FullName), manifest.AppliedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return TryCreateSlotInfo(
                file.FullName,
                gameDirectory,
                resourceId,
                minimumLength,
                "上次成功替换记录");
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CacheSlotInfo? LocateFromLogs(
        string hostDirectory,
        string resourceId,
        string gameDirectory,
        long minimumLength)
    {
        string logDirectory = Path.Combine(hostDirectory, "Logs");
        if (!Directory.Exists(logDirectory)) return null;

        List<DateTime> requestTimes = [];
        foreach (string logFile in Directory.EnumerateFiles(logDirectory, "N0va Desktop_*.log"))
        {
            foreach (string line in ReadLinesShared(logFile))
            {
                int marker = line.IndexOf("request Change Wallpaper:" + resourceId, StringComparison.Ordinal);
                if (marker < 0) continue;
                if (TryReadLogTimestamp(line, out DateTime time)) requestTimes.Add(time);
            }
        }

        if (requestTimes.Count == 0) return null;

        List<(DateTime Time, string Path)> currentFiles = [];
        foreach (string logFile in Directory.EnumerateFiles(logDirectory, "DPPlayer_*.log"))
        {
            foreach (string line in ReadLinesShared(logFile))
            {
                int marker = line.IndexOf("Current: ", StringComparison.Ordinal);
                if (marker < 0) continue;
                if (!TryReadLogTimestamp(line, out DateTime time)) continue;

                string path = line[(marker + "Current: ".Length)..].Trim();
                if (!path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)) continue;
                currentFiles.Add((time, path));
            }
        }

        foreach (DateTime requestTime in requestTimes.OrderByDescending(value => value))
        {
            foreach ((DateTime time, string path) in currentFiles
                         .Where(item =>
                             item.Time >= requestTime &&
                             item.Time <= requestTime.AddSeconds(45))
                         .OrderBy(item => item.Time))
            {
                string normalized = path.Replace('/', Path.DirectorySeparatorChar);
                CacheSlotInfo? slot = TryCreateSlotInfo(
                    normalized,
                    gameDirectory,
                    resourceId,
                    minimumLength,
                    "N0va 播放器日志");
                if (slot is not null) return slot;
            }
        }

        return null;
    }

    private static CacheSlotInfo? LocateFromRecentCache(
        string configFile,
        string gameDirectory,
        string resourceId,
        long minimumLength)
    {
        List<FileInfo> candidates = Directory.EnumerateFiles(gameDirectory, "*.ndf")
            .Select(path => new FileInfo(path))
            .Where(file => file.Length >= minimumLength + NdfBuilder.NdfHeaderLength + 8)
            .Where(file => NdfBuilder.IsVideoNdf(file.FullName))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        if (candidates.Count == 0) return null;

        DateTime configTime = File.GetLastWriteTimeUtc(configFile);
        List<FileInfo> nearConfig = candidates
            .Where(file =>
                file.LastWriteTimeUtc >= configTime.AddMinutes(-20) &&
                file.LastWriteTimeUtc <= configTime.AddMinutes(10))
            .ToList();
        FileInfo? selected = SelectUnique(candidates, nearConfig);

        if (selected is null)
        {
            try
            {
                DateTime[] starts = Process.GetProcessesByName("N0vaDesktop")
                    .Select(process =>
                    {
                        using (process) return process.StartTime.ToUniversalTime();
                    })
                    .ToArray();
                if (starts.Length > 0)
                {
                    DateTime earliestStart = starts.Min();
                    List<FileInfo> afterStart = candidates
                        .Where(file => file.LastWriteTimeUtc >= earliestStart.AddSeconds(-5))
                        .ToList();
                    selected = SelectUnique(candidates, afterStart);
                }
            }
            catch
            {
                // Log and timestamp discovery are enough when process metadata is unavailable.
            }
        }

        if (selected is null) return null;
        return TryCreateSlotInfo(
            selected.FullName,
            gameDirectory,
            resourceId,
            minimumLength,
            "配置时间与缓存时间匹配");
    }

    private static FileInfo? SelectUnique(
        IReadOnlyList<FileInfo> allCandidates,
        IReadOnlyList<FileInfo> matchingCandidates)
    {
        if (matchingCandidates.Count == 0) return null;
        if (matchingCandidates.Count == 1) return matchingCandidates[0];

        FileInfo newest = matchingCandidates
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .First();
        FileInfo? second = allCandidates
            .Where(file => !string.Equals(file.FullName, newest.FullName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        return second is null ||
               newest.LastWriteTimeUtc - second.LastWriteTimeUtc >= TimeSpan.FromMinutes(3)
            ? newest
            : null;
    }

    private static CacheSlotInfo? TryCreateSlotInfo(
        string path,
        string gameDirectory,
        string resourceId,
        long minimumLength,
        string discoverySource)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(
                Path.GetFullPath(gameDirectory),
                fullPath);
            if (relative.StartsWith("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative) ||
                !string.Equals(Path.GetExtension(fullPath), ".ndf", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                return null;
            }

            FileInfo file = new(fullPath);
            if (file.Length < minimumLength + NdfBuilder.NdfHeaderLength + 8 ||
                !NdfBuilder.IsVideoNdf(fullPath))
            {
                return null;
            }

            return new CacheSlotInfo
            {
                ResourceId = resourceId,
                FilePath = fullPath,
                Length = file.Length,
                Sha256 = HashFile(fullPath),
                CreationTimeUtc = file.CreationTimeUtc,
                LastWriteTimeUtc = file.LastWriteTimeUtc,
                DiscoverySource = discoverySource
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static bool TryReadLogTimestamp(string line, out DateTime time)
    {
        Match match = LogTimestampRegex().Match(line);
        return DateTime.TryParseExact(
            match.Groups["time"].Value,
            "yyyy-MM-dd HH:mm:ss.fff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out time);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [GeneratedRegex(
        @"(?m)^cur_sel_res_id=(?<id>[0-9a-fA-F]+)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SelectedResourceRegex();

    [GeneratedRegex(
        @"^\[(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex LogTimestampRegex();
}
