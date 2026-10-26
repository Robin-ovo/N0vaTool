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
    if (live)
    {
        CacheSlotInfo liveSlot = CacheSlotLocator.Locate(
            @"C:\Program Files\N0vaDesktop",
            sourceLength)
            ?? throw new InvalidOperationException("live cache-slot discovery failed");
        Console.WriteLine(
            $"LIVE slot={liveSlot.FileName} length={liveSlot.Length} source={liveSlot.DiscoverySource}");
    }
    Console.WriteLine("OK: dynamic NDF sizes, invalid-slot rejection, and cache-slot discovery");
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
