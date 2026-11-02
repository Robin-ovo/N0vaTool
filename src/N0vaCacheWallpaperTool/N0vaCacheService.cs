using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace N0vaCacheWallpaperTool;

internal sealed partial class N0vaCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _hostDirectory;
    private readonly string _hostExecutable;
    private readonly string _playerExecutable;
    private readonly string _gameDirectory;
    private readonly string _configFile;
    private readonly bool _manageProcesses;
    private readonly bool _validateVersion;

    public static string HostDirectory => @"C:\Program Files\N0vaDesktop";
    public static string HostExecutable => Path.Combine(HostDirectory, "N0vaDesktop.exe");
    public static string PlayerExecutable => Path.Combine(HostDirectory, "DPPlayer.exe");
    public static string GameDirectory =>
        Path.Combine(HostDirectory, "N0vaDesktopCache", "game");
    public static string ConfigFile => Path.Combine(HostDirectory, "config.ini");

    public N0vaCacheService()
        : this(HostDirectory, manageProcesses: true, validateVersion: true)
    {
    }

    internal N0vaCacheService(
        string hostDirectory,
        bool manageProcesses = false,
        bool validateVersion = false)
    {
        _hostDirectory = Path.GetFullPath(hostDirectory);
        _hostExecutable = Path.Combine(_hostDirectory, "N0vaDesktop.exe");
        _playerExecutable = Path.Combine(_hostDirectory, "DPPlayer.exe");
        _gameDirectory = Path.Combine(_hostDirectory, "N0vaDesktopCache", "game");
        _configFile = Path.Combine(_hostDirectory, "config.ini");
        _manageProcesses = manageProcesses;
        _validateVersion = validateVersion;
    }

    public string CurrentHostDirectory => _hostDirectory;
    public string CurrentGameDirectory => _gameDirectory;

    public N0vaInstallationStatus GetInstallationStatus()
    {
        string version = ReadFileVersion();
        bool compatibleVersion =
            !_validateVersion || IsCompatibleVersion(version);

        if (!File.Exists(_hostExecutable))
        {
            return CreateStatus(
                N0vaInstallationIssue.MissingExecutable,
                version,
                compatibleVersion,
                "未找到 N0vaDesktop.exe",
                $"检查位置：{_hostExecutable}\n\n" +
                "如果人工桌面安装在非默认目录，请点击“选择安装目录”。");
        }

        if (!compatibleVersion)
        {
            return CreateStatus(
                N0vaInstallationIssue.UnsupportedVersion,
                version,
                false,
                "N0va Desktop 版本不兼容",
                $"检测到的版本：{(string.IsNullOrWhiteSpace(version) ? "无法读取" : version)}\n" +
                "当前版本支持 N0va Desktop 2.2.x。\n" +
                "如果检测路径不对，请点击“选择安装目录”。");
        }

        if (!File.Exists(_playerExecutable))
        {
            return CreateStatus(
                N0vaInstallationIssue.MissingPlayer,
                version,
                compatibleVersion,
                "缺少 DPPlayer.exe",
                $"检查位置：{_playerExecutable}\n\n" +
                "请确认人工桌面安装完整，或检查安全软件是否隔离了该文件。");
        }

        if (!File.Exists(_configFile))
        {
            return CreateStatus(
                N0vaInstallationIssue.MissingConfig,
                version,
                compatibleVersion,
                "缺少 N0va 配置文件",
                $"检查位置：{_configFile}\n\n" +
                "请先正常启动一次人工桌面，等待配置初始化后再打开本工具。");
        }

        N0vaSelectionInfo selection;
        try
        {
            selection = CacheSlotLocator.ReadSelection(_configFile);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            ToolLogger.Error("读取 N0va 当前壁纸配置失败", ex);
            return CreateStatus(
                N0vaInstallationIssue.AccessDenied,
                version,
                compatibleVersion,
                "无法读取 N0va 当前壁纸配置",
                ex.Message);
        }

        if (!selection.FieldPresent || string.IsNullOrWhiteSpace(selection.ResourceId))
        {
            return CreateStatus(
                N0vaInstallationIssue.MissingResourceSelection,
                version,
                compatibleVersion,
                "配置中尚未选择壁纸",
                $"检查位置：{_configFile}\n\n" +
                "请启动人工桌面并选择一个官方动态壁纸，等待配置写入后再试。");
        }

        if (selection.ResourceType == 0 || !selection.IsHexResourceId)
        {
            return CreateStatus(
                N0vaInstallationIssue.StaticWallpaperSelected,
                version,
                compatibleVersion,
                $"当前为静态壁纸：{selection.ResourceId}",
                $"当前资源：{selection.ResourceId}\n" +
                $"cur_sel_res_type={selection.ResourceType?.ToString() ?? "缺失"}\n\n" +
                "这个状态不是配置缺失，也不是安装不完整。\n" +
                "请先在人工桌面选择一个官方动态壁纸，确认开始播放后再回来替换。");
        }

        if (selection.ResourceType != 1)
        {
            return CreateStatus(
                N0vaInstallationIssue.InvalidResourceSelection,
                version,
                compatibleVersion,
                "当前壁纸资源类型不受支持",
                $"当前资源：{selection.ResourceId}\n" +
                $"cur_sel_res_type={selection.ResourceType?.ToString() ?? "缺失"}\n\n" +
                "请重新选择一个官方动态壁纸后再试。");
        }

        if (!Directory.Exists(_gameDirectory))
        {
            return CreateStatus(
                N0vaInstallationIssue.MissingCacheDirectory,
                version,
                compatibleVersion,
                "请先选择官方动态壁纸",
                $"检查位置：{_gameDirectory}\n\n" +
                "安装本身可能没有问题。请启动人工桌面，并选择一个官方动态壁纸；" +
                "开始播放后再回到本工具。");
        }

        try
        {
            bool hasVideo = Directory
                .EnumerateFiles(_gameDirectory, "*.ndf", SearchOption.TopDirectoryOnly)
                .Any(NdfBuilder.IsVideoNdf);
            if (!hasVideo)
            {
                return CreateStatus(
                    N0vaInstallationIssue.MissingVideoCache,
                    version,
                    compatibleVersion,
                    "请先选择官方动态壁纸",
                    $"缓存目录：{_gameDirectory}\n\n" +
                    "目录中还没有可识别的视频 NDF。请选择一个官方动态壁纸，" +
                    "确认开始播放后再回来。");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            ToolLogger.Error("读取动态壁纸缓存目录失败", ex);
            return CreateStatus(
                N0vaInstallationIssue.AccessDenied,
                version,
                compatibleVersion,
                "没有权限读取动态壁纸缓存",
                ex.Message);
        }

        return CreateStatus(
            N0vaInstallationIssue.Ready,
            version,
            true,
            "N0va Desktop 已就绪",
            $"安装目录：{_hostDirectory}\n版本：{version}");
    }

    public static bool IsCompatibleVersion(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion)) return false;
        Match match = NumericVersionRegex().Match(rawVersion);
        if (!match.Success) return false;
        return int.TryParse(match.Groups["major"].Value, out int major) &&
               int.TryParse(match.Groups["minor"].Value, out int minor) &&
               major == 2 &&
               minor == 2;
    }

    public string DescribeCurrentSlot()
    {
        N0vaInstallationStatus installation = GetInstallationStatus();
        if (!installation.IsReady) return installation.Summary;

        try
        {
            CacheSlotInfo? slot = CacheSlotLocator.Locate(_hostDirectory, 0);
            if (slot is null)
            {
                return "未定位当前动态缓存槽，请在人工桌面选择一个官方动态壁纸";
            }

            string state = "官方动态缓存槽";
            if (File.Exists(ToolPaths.ActiveSlotManifest))
            {
                try
                {
                    ActiveSlotManifest? manifest = JsonSerializer.Deserialize<ActiveSlotManifest>(
                        File.ReadAllText(ToolPaths.ActiveSlotManifest),
                        JsonOptions);
                    if (manifest is not null &&
                        string.Equals(
                            Path.GetFullPath(manifest.SlotPath),
                            Path.GetFullPath(slot.FilePath),
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            HashFile(slot.FilePath),
                            manifest.AppliedSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        state = "当前槽已有替换视频";
                    }
                }
                catch
                {
                    // State text is informational; slot validation remains authoritative.
                }
            }

            return $"{state} · {slot.FileName} · {FormatMiB(slot.Length)}";
        }
        catch (Exception ex)
        {
            return "状态读取失败：" + ex.Message;
        }
    }

    public CacheOperationResult Apply(WallpaperProfile profile)
    {
        using Mutex operationLock = new(false, @"Local\N0vaCacheWallpaperTool.Operation");
        if (!operationLock.WaitOne(0))
        {
            throw new InvalidOperationException("另一个壁纸替换任务正在运行，请稍后重试。");
        }

        if (!string.Equals(profile.Type, "动态", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("这个缓存槽只能应用动态 MP4 壁纸。");
        }
        ToolLogger.Info(
            $"开始替换壁纸：{profile.Title}，安装目录：{_hostDirectory}，源文件：{profile.SourcePath}");
        EnsureInstalled();
        string resourceId = CacheSlotLocator.ReadSelectedResourceId(_configFile);
        CacheSlotInfo? slot = CacheSlotLocator.Locate(_hostDirectory, 0);
        bool n0vaWasRunning = IsN0vaRunning();

        if (slot is null)
        {
            if (!n0vaWasRunning)
            {
                throw SlotNotFound();
            }

            try
            {
                StopN0va();
                slot = CacheSlotLocator.Locate(_hostDirectory, 0);
            }
            finally
            {
                if (slot is null) StartN0vaQuietly();
            }
        }

        if (slot is null)
        {
            throw SlotNotFound();
        }

        NdfBuilder.ValidateSource(profile.SourcePath, slot.Length);
        string workFile = Path.Combine(
            _gameDirectory,
            "." + slot.FileName + ".n0vatool-" + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            NdfBuilder.Build(profile.SourcePath, workFile, slot.Length);
            string replacementHash = HashFile(workFile);
            string originalHash = HashFile(slot.FilePath);
            if (!string.IsNullOrWhiteSpace(slot.Sha256) &&
                !string.Equals(originalHash, slot.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "缓存槽在准备期间发生变化。请在人工桌面重新选择官方动态壁纸后重试。");
            }

            if (IsN0vaRunning())
            {
                StopN0va();
            }

            string currentResourceId = CacheSlotLocator.ReadSelectedResourceId(_configFile);
            if (!string.Equals(resourceId, currentResourceId, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(slot.FilePath) ||
                new FileInfo(slot.FilePath).Length != slot.Length ||
                !string.Equals(HashFile(slot.FilePath), originalHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "人工桌面的当前壁纸在替换过程中发生了变化，请重试。缓存没有被修改。");
            }

            string slotBackup = CaptureSlotBackup(
                slot,
                originalHash,
                currentResourceId);
            BackupConfig();

            try
            {
                File.Move(workFile, slot.FilePath, true);
                File.SetCreationTimeUtc(slot.FilePath, slot.CreationTimeUtc);
                File.SetLastWriteTimeUtc(slot.FilePath, slot.LastWriteTimeUtc);

                if (!NdfBuilder.IsVideoNdf(slot.FilePath) ||
                    !string.Equals(HashFile(slot.FilePath), replacementHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("写入后的缓存校验失败，正在自动回滚。");
                }

                SaveActiveSlotManifest(
                    slot,
                    currentResourceId,
                    replacementHash,
                    originalHash,
                    slotBackup);
            }
            catch
            {
                TryRestoreFile(slotBackup, slot.FilePath);
                throw;
            }

            ToolLogger.Info(
                $"替换完成：槽={slot.FileName}，资源ID={currentResourceId}，" +
                $"发现方式={slot.DiscoverySource}，新哈希={replacementHash}");
            return new CacheOperationResult
            {
                ResourceId = currentResourceId,
                SlotName = slot.FileName,
                SlotLength = slot.Length,
                DiscoverySource = slot.DiscoverySource
            };
        }
        finally
        {
            TryDelete(workFile);
        }
    }

    public CacheOperationResult RestoreOfficial()
    {
        using Mutex operationLock = new(false, @"Local\N0vaCacheWallpaperTool.Operation");
        if (!operationLock.WaitOne(0))
        {
            throw new InvalidOperationException("另一个壁纸替换任务正在运行，请稍后重试。");
        }

        EnsureInstalled();
        ActiveSlotManifest manifest = LoadActiveSlotManifest()
            ?? throw new InvalidOperationException("没有找到由本工具保存的当前槽官方备份。");
        string currentResourceId = CacheSlotLocator.ReadSelectedResourceId(_configFile);
        if (!string.Equals(
                manifest.ResourceId,
                currentResourceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "人工桌面当前已切换为另一条壁纸，不能把旧槽的备份恢复到新壁上。\n" +
                "请先选择要恢复的那条官方壁纸，再重新打开本工具。");
        }
        if (!File.Exists(manifest.OriginalBackupPath) ||
            !File.Exists(manifest.SlotPath))
        {
            throw new FileNotFoundException(
                "当前缓存槽或它的官方备份已被删除，无法恢复。",
                manifest.SlotPath);
        }
        if (new FileInfo(manifest.OriginalBackupPath).Length != manifest.SlotLength)
        {
            throw new InvalidDataException("当前槽官方备份大小不正确，已停止恢复。");
        }

        ToolLogger.Info(
            $"开始恢复当前槽官方备份：槽={Path.GetFileName(manifest.SlotPath)}，" +
            $"资源ID={currentResourceId}");
        StopN0va();
        BackupConfig();
        string temp = manifest.SlotPath + ".n0vatool-restore-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(manifest.OriginalBackupPath, temp, false);
            if (!string.Equals(
                    HashFile(temp),
                    manifest.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("当前槽官方备份校验失败，已停止恢复。");
            }
            File.Move(temp, manifest.SlotPath, true);
            File.SetCreationTimeUtc(
                manifest.SlotPath,
                File.GetCreationTimeUtc(manifest.OriginalBackupPath));
            File.SetLastWriteTimeUtc(
                manifest.SlotPath,
                File.GetLastWriteTimeUtc(manifest.OriginalBackupPath));
            TryDelete(ToolPaths.ActiveSlotManifest);
            ToolLogger.Info(
                $"恢复完成：槽={Path.GetFileName(manifest.SlotPath)}，" +
                $"哈希={manifest.OriginalSha256}");

            return new CacheOperationResult
            {
                ResourceId = currentResourceId,
                SlotName = Path.GetFileName(manifest.SlotPath),
                SlotLength = manifest.SlotLength,
                DiscoverySource = "当前槽官方备份"
            };
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public void LaunchN0va()
    {
        if (!File.Exists(_hostExecutable))
        {
            throw new FileNotFoundException("找不到 N0vaDesktop.exe。", _hostExecutable);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            Arguments = "\"" + _hostExecutable + "\"",
            UseShellExecute = true
        });
    }

    public N0vaStartupRepairResult RepairStartupState()
    {
        if (!File.Exists(_hostExecutable))
        {
            throw new FileNotFoundException(
                "找不到 N0vaDesktop.exe，请先选择正确的安装目录。",
                _hostExecutable);
        }

        ToolLogger.Info($"开始修复人工桌面启动状态，安装目录：{_hostDirectory}");
        return N0vaStartupRepairer.Repair(_hostDirectory);
    }

    public static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private string CaptureSlotBackup(
        CacheSlotInfo slot,
        string currentHash,
        string resourceId)
    {
        ActiveSlotManifest? existing = LoadActiveSlotManifest();
        if (existing is not null &&
            string.Equals(existing.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Path.GetFullPath(existing.SlotPath),
                Path.GetFullPath(slot.FilePath),
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.AppliedSha256, currentHash, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(existing.OriginalBackupPath))
        {
            return existing.OriginalBackupPath;
        }

        string folder = Path.Combine(
            ToolPaths.SlotBackupDirectory,
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string backup = Path.Combine(folder, "original.ndf");
        File.Copy(slot.FilePath, backup, false);
        string backupHash = HashFile(backup);
        if (!string.Equals(backupHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("缓存槽备份校验失败，已停止替换。");
        }

        File.WriteAllText(
            Path.Combine(folder, "manifest.json"),
            JsonSerializer.Serialize(
                new
                {
                    resourceId,
                    slot.FilePath,
                    slot.FileName,
                    slot.Length,
                    sha256 = backupHash,
                    capturedAt = DateTime.Now
                },
                JsonOptions),
            new UTF8Encoding(false));
        File.Copy(
            _configFile,
            Path.Combine(folder, "config.ini"),
            false);
        return backup;
    }

    private static void SaveActiveSlotManifest(
        CacheSlotInfo slot,
        string resourceId,
        string replacementHash,
        string originalHash,
        string originalBackup)
    {
        ToolPaths.EnsureCreated();
        ActiveSlotManifest? existing = LoadActiveSlotManifest();
        bool reuseOriginal = existing is not null &&
            string.Equals(
                Path.GetFullPath(existing.OriginalBackupPath),
                Path.GetFullPath(originalBackup),
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(existing.OriginalSha256);

        ActiveSlotManifest manifest = new()
        {
            Version = 1,
            ResourceId = resourceId,
            SlotPath = slot.FilePath,
            SlotLength = slot.Length,
            AppliedSha256 = replacementHash,
            OriginalSha256 = reuseOriginal ? existing!.OriginalSha256 : originalHash,
            OriginalBackupPath = originalBackup,
            OriginalFileName = slot.FileName,
            CapturedAt = reuseOriginal ? existing!.CapturedAt : DateTime.Now
        };

        string temp = ToolPaths.ActiveSlotManifest + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false));
        File.Move(temp, ToolPaths.ActiveSlotManifest, true);
    }

    private static ActiveSlotManifest? LoadActiveSlotManifest()
    {
        if (!File.Exists(ToolPaths.ActiveSlotManifest)) return null;
        try
        {
            return JsonSerializer.Deserialize<ActiveSlotManifest>(
                File.ReadAllText(ToolPaths.ActiveSlotManifest),
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void BackupConfig()
    {
        ToolPaths.EnsureCreated();
        string path = Path.Combine(
            ToolPaths.BackupDirectory,
            "config-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" +
            Guid.NewGuid().ToString("N")[..6] + ".ini");
        File.Copy(_configFile, path, false);
    }

    private void StopN0va()
    {
        if (!_manageProcesses) return;
        foreach (string name in new[] { "N0vaDesktop", "DPPlayer" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!IsN0vaRunning()) return;
            Thread.Sleep(250);
        }
        throw new TimeoutException("人工桌面没有在预期时间内退出。");
    }

    private bool IsN0vaRunning()
    {
        if (!_manageProcesses) return false;
        bool running = false;
        foreach (string name in new[] { "N0vaDesktop", "DPPlayer" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (!process.HasExited) running = true;
                }
            }
        }
        return running;
    }

    private void StartN0vaQuietly()
    {
        try
        {
            LaunchN0va();
        }
        catch
        {
        }
    }

    private static void TryRestoreFile(string backup, string destination)
    {
        try
        {
            File.Copy(backup, destination, true);
        }
        catch
        {
            // The original backup remains available under the tool data directory.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A later operation can clean up the uniquely named temporary file.
        }
    }

    private static string FormatMiB(long bytes)
    {
        return $"{bytes / 1024d / 1024d:F1} MiB";
    }

    private void EnsureInstalled()
    {
        LogCurrentSelection();
        N0vaInstallationStatus status = GetInstallationStatus();
        if (!status.IsReady)
            throw new InvalidOperationException(status.UserMessage);
    }

    private void LogCurrentSelection()
    {
        if (!File.Exists(_configFile)) return;
        try
        {
            N0vaSelectionInfo selection = CacheSlotLocator.ReadSelection(_configFile);
            ToolLogger.Info(
                $"当前壁纸选择：字段存在={selection.FieldPresent}，" +
                $"资源={selection.ResourceId}，" +
                $"类型={selection.ResourceType?.ToString() ?? "缺失"}，" +
                $"动态类型={selection.DynamicType?.ToString() ?? "缺失"}");
        }
        catch (Exception ex)
        {
            ToolLogger.Warning("读取当前壁纸选择失败：" + ex.Message);
        }
    }

    private string ReadFileVersion()
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(_hostExecutable).FileVersion ?? "";
        }
        catch
        {
            return "";
        }
    }

    private N0vaInstallationStatus CreateStatus(
        N0vaInstallationIssue issue,
        string version,
        bool versionCompatible,
        string summary,
        string details)
    {
        return new N0vaInstallationStatus
        {
            Issue = issue,
            HostDirectory = _hostDirectory,
            Version = version,
            VersionCompatible = versionCompatible,
            Summary = summary,
            Details = details
        };
    }

    private static InvalidOperationException SlotNotFound()
    {
        return new InvalidOperationException(
            "没有定位到当前官方动态壁纸缓存槽。\n\n" +
            "请先在人工桌面中选择一个官方动态壁纸，确认它开始播放；" +
            "然后保持人工桌面打开，回到本工具再点“替换到人工桌面”。\n" +
            "如果当前官方视频很短，它的缓存可能放不下这个 MP4，" +
            "请改选一个时长更长的官方动态壁纸。");
    }

    [GeneratedRegex(
        @"(?<!\d)(?<major>\d+)\.(?<minor>\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumericVersionRegex();
}
