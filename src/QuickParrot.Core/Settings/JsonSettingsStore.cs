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

    /// <summary>Returns defaults if the file is missing, unreadable or corrupt.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path) ? Deserialize(File.ReadAllText(_path)) : new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
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

    public static AppSettings Deserialize(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Sanitized();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
