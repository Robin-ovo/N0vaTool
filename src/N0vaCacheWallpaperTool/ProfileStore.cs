using System.Text.Json;

namespace N0vaCacheWallpaperTool;

internal sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private ToolDatabase _database = new();

    public ProfileStore()
    {
        ToolPaths.EnsureCreated();
        Load();
    }

    public IReadOnlyList<WallpaperProfile> Snapshot()
    {
        lock (_gate)
        {
            return _database.Profiles.Select(Clone).ToList();
        }
    }

    public WallpaperProfile? Find(string id)
    {
        lock (_gate)
        {
            WallpaperProfile? profile = _database.Profiles.FirstOrDefault(item => item.Id == id);
            return profile is null ? null : Clone(profile);
        }
    }

    public void Add(WallpaperProfile profile)
    {
        lock (_gate)
        {
            _database.Profiles.Add(Clone(profile));
            SaveUnsafe();
        }
    }

    public void Update(WallpaperProfile profile)
    {
        lock (_gate)
        {
            int index = _database.Profiles.FindIndex(item => item.Id == profile.Id);
            if (index < 0)
            {
                throw new InvalidOperationException("找不到要保存的壁纸记录。");
            }
            _database.Profiles[index] = Clone(profile);
            SaveUnsafe();
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            int index = _database.Profiles.FindIndex(item => item.Id == id);
            if (index < 0) return false;
            WallpaperProfile removed = _database.Profiles[index];
            _database.Profiles.RemoveAt(index);
            if (_database.LastAppliedId == id) _database.LastAppliedId = null;
            SaveUnsafe();

            if (File.Exists(removed.SourcePath) &&
                removed.SourcePath.StartsWith(ToolPaths.MediaDirectory, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(removed.SourcePath);
            }
            return true;
        }
    }

    public void MarkApplied(string id)
    {
        lock (_gate)
        {
            WallpaperProfile? profile = _database.Profiles.FirstOrDefault(item => item.Id == id);
            if (profile is not null)
            {
                profile.LastAppliedAt = DateTime.Now;
            }
            _database.LastAppliedId = id;
            SaveUnsafe();
        }
    }

    public string? LastAppliedId
    {
        get
        {
            lock (_gate) return _database.LastAppliedId;
        }
    }

    private void Load()
    {
        if (!File.Exists(ToolPaths.ProfilesFile)) return;

        try
        {
            ToolDatabase? stored = JsonSerializer.Deserialize<ToolDatabase>(
                File.ReadAllText(ToolPaths.ProfilesFile),
                JsonOptions);
            if (stored is not null)
            {
                _database = stored;
            }
        }
        catch
        {
            string broken = ToolPaths.ProfilesFile + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(ToolPaths.ProfilesFile, broken, true);
            _database = new ToolDatabase();
        }
    }

    private void SaveUnsafe()
    {
        string json = JsonSerializer.Serialize(_database, JsonOptions);
        string temp = ToolPaths.ProfilesFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, ToolPaths.ProfilesFile, true);
    }

    private static WallpaperProfile Clone(WallpaperProfile source)
    {
        return new WallpaperProfile
        {
            Id = source.Id,
            Title = source.Title,
            Game = source.Game,
            Author = source.Author,
            Type = source.Type,
            SourcePath = source.SourcePath,
            OriginalFileName = source.OriginalFileName,
            SourceLength = source.SourceLength,
            ImportedAt = source.ImportedAt,
            LastAppliedAt = source.LastAppliedAt
        };
    }
}
