using System.Buffers.Binary;
using N0vaCacheWallpaperTool;

string? sourceArgument = null;
bool live = false;
string? emitArgument = null;
for (int index = 0; index < args.Length; index++)
{
    if (string.Equals(args[index], "--live", StringComparison.OrdinalIgnoreCase))
    {
        live = true;
    }
    else if (string.Equals(args[index], "--emit", StringComparison.OrdinalIgnoreCase))
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine("--emit requires an output path");
            return 2;
        }
        emitArgument = args[++index];
    }
    else if (!args[index].StartsWith("--", StringComparison.Ordinal))
    {
        sourceArgument ??= args[index];
    }
}

string? emitPath = emitArgument is not null
    ? Path.GetFullPath(emitArgument)
    : null;

string testRoot = Path.Combine(
    Path.GetTempPath(),
    "N0vaCacheWallpaperTool.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
Environment.SetEnvironmentVariable("N0VA_CACHE_TOOL_DATA_DIR", Path.Combine(testRoot, "data"));
Environment.SetEnvironmentVariable("N0VA_CACHE_TOOL_LOG_DIR", Path.Combine(testRoot, "logs"));
string source = sourceArgument is not null
    ? Path.GetFullPath(sourceArgument)
    : Path.Combine(testRoot, "synthetic-source.mp4");
if (sourceArgument is null)
{
    CreateFakeMp4(source, 128 * 1024);
}
else if (!File.Exists(source))
{
    Console.Error.WriteLine($"source not found: {source}");
    return 2;
}

try
{
    long sourceLength = new FileInfo(source).Length;
    long[] slotLengths = [29928488, 27098591, sourceLength + 4096];
    for (int index = 0; index < slotLengths.Length; index++)
    {
        string output = Path.Combine(testRoot, $"slot-{index}.ndf");
        NdfBuilder.Build(source, output, slotLengths[index]);
        VerifyNdf(output, sourceLength, slotLengths[index]);
    }
    if (emitPath is not null)
    {
        NdfBuilder.Build(source, emitPath, 29928488);
        VerifyNdf(emitPath, sourceLength, 29928488);
        Console.WriteLine($"EMIT {emitPath}");
    }

    string tooSmall = Path.Combine(testRoot, "too-small.ndf");
    bool rejected = false;
    try
    {
        NdfBuilder.Build(source, tooSmall, sourceLength + 9);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }
    Require(rejected, "too-small slot was not rejected");

    VerifyReplacementDeletionRediscovery(source, sourceLength, testRoot);
    VerifyInstallationDetectionAndLogs(source, sourceLength, testRoot);
    VerifyErrorSummary();
    if (live)
    {
        IReadOnlyList<N0vaInstallationCandidate> liveCandidates =
            N0vaInstallationLocator.FindCandidates(null);
        Require(
            liveCandidates.Any(candidate =>
                string.Equals(
                    candidate.HostDirectory,
                    @"C:\Program Files\N0vaDesktop",
                    StringComparison.OrdinalIgnoreCase)),
            "live automatic installation detection did not find the current N0va install");

        string? liveServiceName = N0vaStartupRepairer.FindServiceName();
        Console.WriteLine($"LIVE service={liveServiceName ?? "not found"}");
        Require(
            !string.IsNullOrWhiteSpace(liveServiceName),
            "live N0va service discovery failed");

        N0vaCacheService liveService = new(
            @"C:\Program Files\N0vaDesktop",
            manageProcesses: false,
            validateVersion: true);
        N0vaInstallationStatus liveStatus = liveService.GetInstallationStatus();
        Console.WriteLine(
            $"LIVE installation version={liveStatus.Version} status={liveStatus.Summary}");
        Require(
            liveStatus.IsReady ||
            liveStatus.Issue is
                N0vaInstallationIssue.StaticWallpaperSelected or
                N0vaInstallationIssue.MissingResourceSelection,
            "unexpected live installation status: " + liveStatus.UserMessage);
        if (liveStatus.IsReady)
        {
            CacheSlotInfo liveSlot = CacheSlotLocator.Locate(
                @"C:\Program Files\N0vaDesktop",
                sourceLength)
                ?? throw new InvalidOperationException("live cache-slot discovery failed");
            Console.WriteLine(
                $"LIVE slot={liveSlot.FileName} length={liveSlot.Length} source={liveSlot.DiscoverySource}");
        }
    }
    Console.WriteLine(
        "OK: NDF, cache-slot discovery, installation status, manual path, and log deletion");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    string resolved = Path.GetFullPath(testRoot);
    string tempRoot = Path.GetFullPath(Path.GetTempPath());
    if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
    {
        Directory.Delete(resolved, true);
    }
}

static void VerifyNdf(string path, long sourceLength, long expectedLength)
{
    FileInfo file = new(path);
    Require(file.Length == expectedLength, "NDF length mismatch");
    Require(NdfBuilder.IsVideoNdf(path), "NDF failed video validation");

    using FileStream stream = File.OpenRead(path);
    Span<byte> head = stackalloc byte[12];
    stream.ReadExactly(head);
    Require(head[..2].SequenceEqual(new byte[2]), "NDF header is not 00 00");
    Require(head[6..10].SequenceEqual("ftyp"u8), "NDF MP4 ftyp offset is wrong");

    long freeOffset = 2 + sourceLength;
    stream.Position = freeOffset;
    Span<byte> free = stackalloc byte[8];
    stream.ReadExactly(free);
    uint freeLength = BinaryPrimitives.ReadUInt32BigEndian(free);
    Require(free[4..].SequenceEqual("free"u8), "NDF alignment box is not free");
    Require(freeLength == expectedLength - 2 - sourceLength, "NDF free box length is wrong");
}

static void VerifyReplacementDeletionRediscovery(string source, long sourceLength, string testRoot)
{
    string host = Path.Combine(testRoot, "host");
    string cache = Path.Combine(host, "N0vaDesktopCache", "game");
    Directory.CreateDirectory(cache);
    Directory.CreateDirectory(Path.Combine(host, "Logs"));

    string firstSlot = Path.Combine(cache, "first-slot.ndf");
    NdfBuilder.Build(source, firstSlot, sourceLength + 8192);
    File.SetLastWriteTimeUtc(firstSlot, DateTime.UtcNow.AddSeconds(-6));

    string config = Path.Combine(host, "config.ini");
    File.WriteAllText(
        config,
        "[SETTING]\ncur_sel_res_id=111111111111111111111111\n" +
        "cur_sel_res_type=1\ncur_sel_res_dynamic_type=0\n");
    File.SetLastWriteTimeUtc(config, DateTime.UtcNow.AddSeconds(-5));

    CacheSlotInfo first = CacheSlotLocator.Locate(host, sourceLength)
        ?? throw new InvalidOperationException("first slot was not discovered");
    Require(
        string.Equals(
            Path.GetFullPath(first.FilePath),
            Path.GetFullPath(firstSlot),
            StringComparison.OrdinalIgnoreCase),
        "discovered the wrong first slot");

    File.Delete(firstSlot);
    string secondSlot = Path.Combine(cache, "new-current-slot.ndf");
    NdfBuilder.Build(source, secondSlot, sourceLength + 12288);
    File.SetLastWriteTimeUtc(secondSlot, DateTime.UtcNow.AddSeconds(-2));
    File.WriteAllText(
        config,
        "[SETTING]\ncur_sel_res_id=222222222222222222222222\n" +
        "cur_sel_res_type=1\ncur_sel_res_dynamic_type=0\n");
    File.SetLastWriteTimeUtc(config, DateTime.UtcNow);

    CacheSlotInfo second = CacheSlotLocator.Locate(host, sourceLength)
        ?? throw new InvalidOperationException("replacement slot was not rediscovered after deletion");
    Require(
        string.Equals(
            Path.GetFullPath(second.FilePath),
            Path.GetFullPath(secondSlot),
            StringComparison.OrdinalIgnoreCase),
        "rediscovered the deleted slot instead of the new current slot");
}

static void VerifyInstallationDetectionAndLogs(
    string source,
    long sourceLength,
    string testRoot)
{
    Require(N0vaCacheService.IsCompatibleVersion("2.2.1.4"), "2.2.1.4 should be compatible");
    Require(N0vaCacheService.IsCompatibleVersion("2.2.9.1"), "2.2.x should be compatible");
    Require(!N0vaCacheService.IsCompatibleVersion("2.3.0.0"), "2.3 should be rejected");
    Require(!N0vaCacheService.IsCompatibleVersion("1.2.0.0"), "1.2 should be rejected");
    Require(!N0vaCacheService.IsCompatibleVersion("unknown"), "unknown version should be rejected");

    string host = Path.Combine(testRoot, "installation-host");
    Directory.CreateDirectory(host);
    N0vaCacheService service = new(host, manageProcesses: false, validateVersion: false);
    Require(
        service.GetInstallationStatus().Issue == N0vaInstallationIssue.MissingExecutable,
        "missing executable was not reported");

    string executable = Path.Combine(host, "N0vaDesktop.exe");
    File.WriteAllBytes(executable, []);
    N0vaCacheService versionChecked = new(
        host,
        manageProcesses: false,
        validateVersion: true);
    Require(
        versionChecked.GetInstallationStatus().Issue ==
        N0vaInstallationIssue.UnsupportedVersion,
        "unsupported version was not reported");
    Require(
        service.GetInstallationStatus().Issue == N0vaInstallationIssue.MissingPlayer,
        "missing DPPlayer was not reported");

    File.WriteAllBytes(Path.Combine(host, "DPPlayer.exe"), []);
    Require(
        service.GetInstallationStatus().Issue == N0vaInstallationIssue.MissingConfig,
        "missing config was not reported");

    string config = Path.Combine(host, "config.ini");
    File.WriteAllText(
        config,
        "[SETTING]\ncur_sel_res_type=1\ncur_sel_res_dynamic_type=0\n");
    Require(
        service.GetInstallationStatus().Issue ==
        N0vaInstallationIssue.MissingResourceSelection,
        "missing current resource selection was not reported");

    File.WriteAllText(
        config,
        "[SETTING]\r\ncur_sel_res_id=BlackIris_Green\r\n" +
        "cur_sel_res_type=0\r\ncur_sel_res_dynamic_type=0\r\n");
    N0vaInstallationStatus staticStatus = service.GetInstallationStatus();
    Require(
        staticStatus.Issue == N0vaInstallationIssue.StaticWallpaperSelected,
        "static wallpaper was not reported");
    Require(
        staticStatus.UserMessage.Contains("BlackIris_Green", StringComparison.Ordinal) &&
        staticStatus.UserMessage.Contains("官方动态壁纸", StringComparison.Ordinal),
        "static wallpaper guidance was not shown");
    bool staticIdRejected = false;
    try
    {
        CacheSlotLocator.ReadSelectedResourceId(config);
    }
    catch (InvalidDataException ex)
    {
        staticIdRejected = ex.Message.Contains("静态壁纸", StringComparison.Ordinal);
    }
    Require(staticIdRejected, "static resource should not be treated as a dynamic ID");

    File.WriteAllText(
        config,
        "[SETTING]\ncur_sel_res_id=333333333333333333333333\n" +
        "cur_sel_res_type=2\ncur_sel_res_dynamic_type=0\n");
    Require(
        service.GetInstallationStatus().Issue ==
        N0vaInstallationIssue.InvalidResourceSelection,
        "invalid resource type was not reported");

    File.WriteAllText(
        config,
        "[SETTING]\ncur_sel_res_id=333333333333333333333333\n" +
        "cur_sel_res_type=1\ncur_sel_res_dynamic_type=0\n");
    N0vaInstallationStatus missingCache = service.GetInstallationStatus();
    Require(
        missingCache.Issue == N0vaInstallationIssue.MissingCacheDirectory,
        "missing cache directory was not reported");
    Require(
        missingCache.UserMessage.Contains("官方动态壁纸", StringComparison.Ordinal),
        "missing cache guidance was not shown");

    string game = Path.Combine(host, "N0vaDesktopCache", "game");
    Directory.CreateDirectory(game);
    Require(
        service.GetInstallationStatus().Issue == N0vaInstallationIssue.MissingVideoCache,
        "missing video cache was not reported");

    NdfBuilder.Build(
        source,
        Path.Combine(game, "installation-test.ndf"),
        sourceLength + 4096);
    Require(
        service.GetInstallationStatus().IsReady,
        "complete synthetic installation should be ready");

    Require(
        N0vaInstallationLocator.TryNormalizeHostDirectory(executable, out string byExe) &&
        string.Equals(byExe, host, StringComparison.OrdinalIgnoreCase),
        "manual path normalization by executable failed");
    Require(
        N0vaInstallationLocator.TryNormalizeHostDirectory(host, out string byDirectory) &&
        string.Equals(byDirectory, host, StringComparison.OrdinalIgnoreCase),
        "manual path normalization by directory failed");
    Require(
        N0vaInstallationLocator.TryNormalizeHostDirectory(
            "\"" + executable + "\",0",
            out string byDisplayIcon) &&
        string.Equals(byDisplayIcon, host, StringComparison.OrdinalIgnoreCase),
        "registry DisplayIcon path normalization failed");

    IReadOnlyList<N0vaInstallationCandidate> candidates =
        N0vaInstallationLocator.FindCandidates(host);
    Require(
        candidates.Any(candidate =>
            string.Equals(
                candidate.HostDirectory,
                host,
                StringComparison.OrdinalIgnoreCase)),
        "saved manual installation directory was not considered");

    SettingsStore settings = new();
    settings.SetN0vaInstallDirectory(host);
    SettingsStore reloadedSettings = new();
    Require(
        string.Equals(
            reloadedSettings.N0vaInstallDirectory,
            Path.GetFullPath(host),
            StringComparison.OrdinalIgnoreCase),
        "manual installation path was not persisted");

    ToolLogger.Info("test log entry");
    Require(File.Exists(ToolLogger.CurrentFile), "log file was not created");
    Require(ToolLogger.DeleteLogs() >= 1, "log files were not deleted");
    Require(
        !Directory.EnumerateFiles(ToolPaths.LogDirectory, "*.log").Any(),
        "log directory still contains logs");
}

static void VerifyErrorSummary()
{
    string firstLine = ErrorSummary.Format(
        "替换失败",
        "当前为静态壁纸\r\n请选择官方动态壁纸");
    Require(
        firstLine == "替换失败：当前为静态壁纸",
        "error summary did not select the first line");

    string longSummary = ErrorSummary.Format(
        "替换失败",
        new string('错', 200));
    Require(
        longSummary.EndsWith('…') &&
        longSummary.Length < 140,
        "long error summary was not truncated");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CreateFakeMp4(string path, int length)
{
    byte[] data = new byte[length];
    BinaryPrimitives.WriteUInt32BigEndian(data, 24);
    "ftyp"u8.CopyTo(data.AsSpan(4));
    "isom"u8.CopyTo(data.AsSpan(8));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 0);
    "isom"u8.CopyTo(data.AsSpan(16));
    "mp41"u8.CopyTo(data.AsSpan(20));
    File.WriteAllBytes(path, data);
}
