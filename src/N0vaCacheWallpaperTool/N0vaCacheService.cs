using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace N0vaCacheWallpaperTool;

internal sealed class N0vaCacheService
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

    public string CurrentGameDirectory => _gameDirectory;

    public bool IsInstalled =>
        File.Exists(_hostExecutable) &&
        File.Exists(_playerExecutable) &&
        Directory.Exists(_gameDirectory) &&
        File.Exists(_configFile) &&
        IsSupportedVersion();

    public string DescribeCurrentSlot()
    {
        if (!IsInstalled) return "未检测到完整的 N0va Desktop 2.2.1.4";

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
        if (!File.Exists(_hostExecutable) ||
            !File.Exists(_playerExecutable) ||
            !Directory.Exists(_gameDirectory) ||
            !File.Exists(_configFile) ||
            !IsSupportedVersion())
        {
            throw new InvalidOperationException(
                "没有找到完整的 N0va Desktop 2.2.1.4 安装。" +
                "请确认安装路径仍为 C:\\Program Files\\N0vaDesktop。");
        }
    }

    private bool IsSupportedVersion()
    {
        if (!_validateVersion) return true;
        try
        {
            return string.Equals(
                FileVersionInfo.GetVersionInfo(_hostExecutable).FileVersion,
                "2.2.1.4",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
}
