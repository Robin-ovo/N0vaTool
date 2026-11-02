using System.Buffers.Binary;
using System.Text;
using N0vaCacheWallpaperTool;

if (args.Length > 2)
{
    Console.Error.WriteLine("usage: integration [source.mp4] [result.log]");
    return 2;
}

string testRoot = Path.Combine(
    Path.GetTempPath(),
    "N0vaCacheWallpaperTool.Integration-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
string source = args.Length >= 1
    ? Path.GetFullPath(args[0])
    : Path.Combine(testRoot, "synthetic-source.mp4");
string? resultFile = args.Length >= 2 ? Path.GetFullPath(args[1]) : null;
if (args.Length == 0)
{
    CreateFakeMp4(source, 512 * 1024, 0x10);
}
else if (!File.Exists(source))
{
    Console.Error.WriteLine($"source not found: {source}");
    return 2;
}

string host = Path.Combine(testRoot, "host");
string game = Path.Combine(host, "N0vaDesktopCache", "game");
Directory.CreateDirectory(game);
Directory.CreateDirectory(Path.Combine(host, "Logs"));
File.WriteAllBytes(Path.Combine(host, "N0vaDesktop.exe"), []);
File.WriteAllBytes(Path.Combine(host, "DPPlayer.exe"), []);
Environment.SetEnvironmentVariable(
    "N0VA_CACHE_TOOL_DATA_DIR",
    Path.Combine(testRoot, "data"));
Environment.SetEnvironmentVariable(
    "N0VA_CACHE_TOOL_LOG_DIR",
    Path.Combine(testRoot, "logs"));

StringBuilder result = new();
void Record(string value)
{
    result.AppendLine(value);
    if (resultFile is not null)
    {
        File.WriteAllText(resultFile, result.ToString(), new UTF8Encoding(false));
    }
    Console.WriteLine(value);
}

try
{
    long sourceLength = new FileInfo(source).Length;
    string firstOfficial = Path.Combine(testRoot, "official-a.mp4");
    CreateFakeMp4(firstOfficial, 64 * 1024, 0x41);

    string firstSlot = Path.Combine(game, "1785918516531_721.ndf");
    long firstLength = sourceLength + 256 * 1024;
    NdfBuilder.Build(firstOfficial, firstSlot, firstLength);
    string firstHash = N0vaCacheService.HashFile(firstSlot);
    WriteConfig(host, "6a72f536719cabb79213aa00", DateTime.UtcNow.AddSeconds(-2));
    File.SetLastWriteTimeUtc(firstSlot, DateTime.UtcNow.AddSeconds(-3));

    N0vaCacheService service = new(host);
    CacheSlotInfo initial = CacheSlotLocator.Locate(host, 0)
        ?? throw new InvalidOperationException("initial synthetic slot was not found");
    Record($"initialSlot={initial.FileName}");
    Record($"initialSha256={firstHash}");

    CacheOperationResult firstApplied = service.Apply(new WallpaperProfile
    {
        Title = "first replacement",
        Type = "动态",
        SourcePath = source
    });
    string firstAppliedHash = N0vaCacheService.HashFile(firstSlot);
    Require(firstAppliedHash != firstHash, "first apply did not change the slot");
    Require(NdfBuilder.IsVideoNdf(firstSlot), "first apply produced an invalid NDF");
    Record($"firstAppliedSlot={firstApplied.SlotName}");
    Record($"firstAppliedSha256={firstAppliedHash}");
    Record($"configIdAfterApply={CacheSlotLocator.ReadSelectedResourceId(Path.Combine(host, "config.ini"))}");

    CacheOperationResult restored = service.RestoreOfficial();
    string restoredHash = N0vaCacheService.HashFile(firstSlot);
    Require(restoredHash == firstHash, "restore did not recover the first official slot");
    Record($"restoredSlot={restored.SlotName}");
    Record($"restoredSha256={restoredHash}");

    File.Delete(firstSlot);
    string secondOfficial = Path.Combine(testRoot, "official-b.mp4");
    CreateFakeMp4(secondOfficial, 96 * 1024, 0x42);
    string secondSlot = Path.Combine(game, "1785919999999_999.ndf");
    long secondLength = sourceLength + 384 * 1024;
    NdfBuilder.Build(secondOfficial, secondSlot, secondLength);
    string secondHash = N0vaCacheService.HashFile(secondSlot);
    WriteConfig(host, "6a72f536719cabb79213aa99", DateTime.UtcNow);
    File.SetLastWriteTimeUtc(secondSlot, DateTime.UtcNow.AddSeconds(-1));

    CacheSlotInfo rediscovered = CacheSlotLocator.Locate(host, 0)
        ?? throw new InvalidOperationException("replacement slot was not found after deletion");
    Require(
        string.Equals(
            rediscovered.FileName,
            Path.GetFileName(secondSlot),
            StringComparison.OrdinalIgnoreCase),
        "locator picked the deleted slot instead of the new current slot");
    Record($"rediscoveredSlot={rediscovered.FileName}");

    CacheOperationResult secondApplied = service.Apply(new WallpaperProfile
    {
        Title = "second replacement",
        Type = "动态",
        SourcePath = source
    });
    string secondAppliedHash = N0vaCacheService.HashFile(secondSlot);
    Require(secondAppliedHash != secondHash, "second apply did not change the new slot");
    Record($"secondAppliedSlot={secondApplied.SlotName}");
    Record($"secondAppliedSha256={secondAppliedHash}");

    service.RestoreOfficial();
    Require(
        N0vaCacheService.HashFile(secondSlot) == secondHash,
        "second restore did not recover the new official slot");
    Record($"secondRestoredSha256={secondHash}");
    Record("OK");
    return 0;
}
catch (Exception ex)
{
    Record("ERROR");
    Record(ex.ToString());
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

static void CreateFakeMp4(string path, int length, byte fill)
{
    byte[] data = new byte[length];
    Array.Fill(data, fill);
    BinaryPrimitives.WriteUInt32BigEndian(data, 24);
    "ftyp"u8.CopyTo(data.AsSpan(4));
    "isom"u8.CopyTo(data.AsSpan(8));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 0);
    "isom"u8.CopyTo(data.AsSpan(16));
    "mp41"u8.CopyTo(data.AsSpan(20));
    File.WriteAllBytes(path, data);
}

static void WriteConfig(string host, string resourceId, DateTime lastWriteTimeUtc)
{
    string config = Path.Combine(host, "config.ini");
    File.WriteAllText(
        config,
        "[SETTING]\n" +
        $"cur_sel_res_id={resourceId}\n" +
        "cur_sel_res_type=1\n" +
        "cur_sel_res_dynamic_type=0\n");
    File.SetLastWriteTimeUtc(config, lastWriteTimeUtc);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
