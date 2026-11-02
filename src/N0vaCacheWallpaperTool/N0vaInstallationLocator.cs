using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace N0vaCacheWallpaperTool;

internal sealed record N0vaInstallationCandidate(
    string HostDirectory,
    string Source);

internal static partial class N0vaInstallationLocator
{
    public static IReadOnlyList<N0vaInstallationCandidate> FindCandidates(
        string? preferredDirectory)
    {
        List<N0vaInstallationCandidate> candidates = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string source, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!TryNormalizeHostDirectory(path, out string hostDirectory))
            {
                ToolLogger.Info($"安装路径候选不可用（{source}）：{path}");
                return;
            }
            if (seen.Add(hostDirectory))
            {
                candidates.Add(new N0vaInstallationCandidate(hostDirectory, source));
                ToolLogger.Info($"发现安装路径候选（{source}）：{hostDirectory}");
            }
        }

        AddCandidate("手动保存", preferredDirectory);
        AddRunningProcesses(AddCandidate);
        AddRegistryCandidates(AddCandidate);
        AddStandardDirectories(AddCandidate);

        if (candidates.Count == 0)
        {
            ToolLogger.Warning("未通过手动设置、运行进程、注册表或常见目录找到 N0vaDesktop.exe。");
        }
        return candidates;
    }

    public static bool TryNormalizeHostDirectory(
        string path,
        out string hostDirectory)
    {
        hostDirectory = "";
        if (string.IsNullOrWhiteSpace(path)) return false;

        string value = path.Trim().Trim('"');
        value = TrailingLaunchArgumentRegex().Replace(value, "").Trim().Trim('"');

        try
        {
            string fullPath = Path.GetFullPath(value);
            if (File.Exists(fullPath) &&
                string.Equals(
                    Path.GetFileName(fullPath),
                    "N0vaDesktop.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                hostDirectory = Path.GetDirectoryName(fullPath) ?? "";
            }
            else if (Directory.Exists(fullPath))
            {
                hostDirectory = fullPath;
            }
            else
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(hostDirectory) &&
                   File.Exists(Path.Combine(hostDirectory, "N0vaDesktop.exe"));
        }
        catch
        {
            return false;
        }
    }

    private static void AddRunningProcesses(
        Action<string, string?> addCandidate)
    {
        foreach (Process process in Process.GetProcessesByName("N0vaDesktop"))
        {
            using (process)
            {
                try
                {
                    addCandidate("运行中的 N0vaDesktop", process.MainModule?.FileName);
                }
                catch (Exception ex)
                {
                    ToolLogger.Warning(
                        "读取运行中 N0vaDesktop 路径失败：" + ex.Message);
                }
            }
        }
    }

    private static void AddRegistryCandidates(
        Action<string, string?> addCandidate)
    {
        RegistryHive[] hives = [RegistryHive.CurrentUser, RegistryHive.LocalMachine];
        RegistryView[] views = [RegistryView.Registry64, RegistryView.Registry32];

        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey? uninstall = baseKey.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;

                    foreach (string subKeyName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using RegistryKey? appKey = uninstall.OpenSubKey(subKeyName);
                            if (appKey is null) continue;

                            string displayName =
                                appKey.GetValue("DisplayName") as string ?? "";
                            if (!LooksLikeN0va(displayName)) continue;

                            string source = $"注册表 {hive}/{view}";
                            addCandidate(
                                source,
                                appKey.GetValue("InstallLocation") as string);
                            addCandidate(
                                source,
                                appKey.GetValue("DisplayIcon") as string);
                            addCandidate(
                                source,
                                ExtractExecutable(
                                    appKey.GetValue("UninstallString") as string));
                        }
                        catch (Exception ex)
                        {
                            ToolLogger.Warning(
                                $"读取注册表卸载项 {subKeyName} 失败：" + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ToolLogger.Warning(
                        $"读取注册表位置 {hive}/{view} 失败：" + ex.Message);
                }
            }
        }
    }

    private static void AddStandardDirectories(
        Action<string, string?> addCandidate)
    {
        List<string> roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        ];

        foreach (string root in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            addCandidate("常见安装目录", Path.Combine(root, "N0vaDesktop"));
        }
        addCandidate(
            "工具所在目录",
            Path.Combine(AppContext.BaseDirectory, "N0vaDesktop.exe"));
    }

    private static bool LooksLikeN0va(string displayName)
    {
        return displayName.Contains("N0va", StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains("人工桌面", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        Match quoted = QuotedExecutableRegex().Match(command);
        if (quoted.Success) return quoted.Groups["path"].Value;

        Match plain = PlainExecutableRegex().Match(command);
        return plain.Success ? plain.Groups["path"].Value.Trim() : null;
    }

    [GeneratedRegex(@""",\d+\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingLaunchArgumentRegex();

    [GeneratedRegex(@"""(?<path>[^""]+?\.exe)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedExecutableRegex();

    [GeneratedRegex(@"^\s*(?<path>.+?\.exe)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainExecutableRegex();
}
