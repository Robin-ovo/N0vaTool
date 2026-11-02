using System.Text.Json;

namespace N0vaCacheWallpaperTool;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private ToolSettings _settings = new();

    public SettingsStore()
    {
        ToolPaths.EnsureCreated();
        Load();
    }

    public string? N0vaInstallDirectory
    {
        get
        {
            lock (_gate)
            {
                return string.IsNullOrWhiteSpace(_settings.N0vaInstallDirectory)
                    ? null
                    : _settings.N0vaInstallDirectory;
            }
        }
    }

    public void SetN0vaInstallDirectory(string? directory)
    {
        lock (_gate)
        {
            string value = string.IsNullOrWhiteSpace(directory)
                ? ""
                : Path.GetFullPath(directory);
            if (string.Equals(
                    _settings.N0vaInstallDirectory,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.N0vaInstallDirectory = value;
            SaveUnsafe();
        }
    }

    private void Load()
    {
        if (!File.Exists(ToolPaths.SettingsFile)) return;

        try
        {
            ToolSettings? stored = JsonSerializer.Deserialize<ToolSettings>(
                File.ReadAllText(ToolPaths.SettingsFile),
                JsonOptions);
            if (stored is not null) _settings = stored;
        }
        catch (Exception ex)
        {
            ToolLogger.Error("读取工具设置失败", ex);
            string broken = ToolPaths.SettingsFile + ".broken-" +
                DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(ToolPaths.SettingsFile, broken, true);
            _settings = new ToolSettings();
        }
    }

    private void SaveUnsafe()
    {
        string json = JsonSerializer.Serialize(_settings, JsonOptions);
        string temp = ToolPaths.SettingsFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, ToolPaths.SettingsFile, true);
    }
}
