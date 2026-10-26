namespace N0vaCacheWallpaperTool;

internal static class ToolPaths
{
    public static string Root
    {
        get
        {
            string? overrideDirectory =
                Environment.GetEnvironmentVariable("N0VA_CACHE_TOOL_DATA_DIR");
            return string.IsNullOrWhiteSpace(overrideDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "N0vaCacheWallpaperTool")
                : Path.GetFullPath(overrideDirectory);
        }
    }

    public static string MediaDirectory => Path.Combine(Root, "media");
    public static string BackupDirectory => Path.Combine(Root, "backup");
    public static string SlotBackupDirectory =>
        Path.Combine(BackupDirectory, "slots");
    public static string ActiveSlotManifest =>
        Path.Combine(BackupDirectory, "active-slot.json");
    public static string ProfilesFile => Path.Combine(Root, "profiles.json");
    public static string OfficialSlotBackup =>
        Path.Combine(BackupDirectory, "official_slot.ndf");
    public static string OfficialSlotManifest =>
        Path.Combine(BackupDirectory, "official_slot.json");

    public static string BundledOfficialSlot =>
        Path.Combine(AppContext.BaseDirectory, "assets", "official_slot.ndf");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(MediaDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(SlotBackupDirectory);
    }
}
