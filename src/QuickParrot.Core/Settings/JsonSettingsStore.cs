using System.Text.Json;

namespace QuickParrot.Core.Settings;

/// <summary>Stores settings as JSON, by default in %APPDATA%\QuickParrot\settings.json.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public JsonSettingsStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickParrot", "settings.json");

    public string BackupPath => _path + ".bad";

    /// <summary>
    /// Returns defaults if the file is missing, unreadable or corrupt. A corrupt file is renamed to
    /// <see cref="BackupPath"/> first, so the next save doesn't silently destroy it.
    /// </summary>
    public SettingsLoadResult Load()
    {
        string json;
        try
        {
            if (!File.Exists(_path))
                return new SettingsLoadResult(new AppSettings());

            json = File.ReadAllText(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(new AppSettings(), $"Couldn't read settings, using defaults: {e.Message}");
        }

        if (TryDeserialize(json, out var settings))
            return new SettingsLoadResult(settings);

        return new SettingsLoadResult(new AppSettings(), SetAsideCorruptFile());
    }

    // Written to a temp file and moved into place, so a crash mid-write can't leave a truncated file.
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, Serialize(settings));
        File.Move(tempPath, _path, overwrite: true);
    }

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    public static AppSettings Deserialize(string json) => TryDeserialize(json, out var settings) ? settings : new AppSettings();

    public static bool TryDeserialize(string json, out AppSettings settings)
    {
        AppSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            loaded = null;
        }

        settings = loaded?.Sanitized() ?? new AppSettings();
        return loaded is not null;
    }

    private string SetAsideCorruptFile()
    {
        try
        {
            File.Move(_path, BackupPath, overwrite: true);
            return $"Your settings file was corrupt, so defaults are in use. The old file was kept as {BackupPath}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Your settings file was corrupt, so defaults are in use, and it couldn't be set aside: {e.Message}";
        }
    }
}
