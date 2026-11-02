using System.Text.Json.Serialization;

namespace N0vaCacheWallpaperTool;

internal sealed class WallpaperProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Game { get; set; } = "原神";
    public string Author { get; set; } = "自定义";
    public string Type { get; set; } = "动态";
    public string SourcePath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public long SourceLength { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.Now;
    public DateTime? LastAppliedAt { get; set; }
}

internal sealed class ToolDatabase
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("profiles")]
    public List<WallpaperProfile> Profiles { get; set; } = [];

    [JsonPropertyName("lastAppliedId")]
    public string? LastAppliedId { get; set; }
}

internal sealed class CacheSlotInfo
{
    public string ResourceId { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public long Length { get; init; }
    public string Sha256 { get; init; } = "";
    public DateTime CreationTimeUtc { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public string DiscoverySource { get; init; } = "";
}

internal sealed class ActiveSlotManifest
{
    public int Version { get; set; } = 1;
    public string ResourceId { get; set; } = "";
    public string SlotPath { get; set; } = "";
    public long SlotLength { get; set; }
    public string AppliedSha256 { get; set; } = "";
    public string OriginalSha256 { get; set; } = "";
    public string OriginalBackupPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}

internal sealed class CacheOperationResult
{
    public string ResourceId { get; init; } = "";
    public string SlotName { get; init; } = "";
    public long SlotLength { get; init; }
    public string DiscoverySource { get; init; } = "";
}

internal enum N0vaInstallationIssue
{
    Ready,
    MissingExecutable,
    UnsupportedVersion,
    MissingPlayer,
    MissingConfig,
    MissingResourceSelection,
    StaticWallpaperSelected,
    InvalidResourceSelection,
    MissingCacheDirectory,
    MissingVideoCache,
    AccessDenied,
    Unknown
}

internal sealed class N0vaInstallationStatus
{
    public N0vaInstallationIssue Issue { get; init; }
    public string HostDirectory { get; init; } = "";
    public string Version { get; init; } = "";
    public bool VersionCompatible { get; init; }
    public bool IsReady => Issue == N0vaInstallationIssue.Ready;
    public string Summary { get; init; } = "";
    public string Details { get; init; } = "";

    public string UserMessage =>
        string.IsNullOrWhiteSpace(Details) ? Summary : Summary + "\n\n" + Details;
}

internal sealed class N0vaSelectionInfo
{
    public bool FieldPresent { get; init; }
    public string ResourceId { get; init; } = "";
    public int? ResourceType { get; init; }
    public int? DynamicType { get; init; }
    public bool IsHexResourceId { get; init; }
    public bool IsDynamicSelection =>
        FieldPresent &&
        ResourceType == 1 &&
        IsHexResourceId;
    public bool IsStaticSelection =>
        FieldPresent &&
        (ResourceType == 0 || !IsHexResourceId);
}

internal sealed class ToolSettings
{
    public int Version { get; set; } = 1;
    public string N0vaInstallDirectory { get; set; } = "";
}
