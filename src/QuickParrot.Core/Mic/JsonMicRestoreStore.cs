using System.Text.Json;

namespace QuickParrot.Core.Mic;

/// <summary>Keeps the mic restore record as JSON, by default in %APPDATA%\QuickParrot\mic-restore.json.</summary>
public sealed class JsonMicRestoreStore : IMicRestoreStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;

    public JsonMicRestoreStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickParrot", "mic-restore.json");

    public MicRestoreRecord? Load() => File.Exists(_path) ? Deserialize(File.ReadAllText(_path)) : null;

    // Written to a temp file and moved into place, so a crash mid-write can't leave a truncated record.
    public void Save(MicRestoreRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, Serialize(record));
        File.Move(tempPath, _path, overwrite: true);
    }

    public void Delete() => File.Delete(_path);

    public static string Serialize(MicRestoreRecord record) => JsonSerializer.Serialize(record, JsonOptions);

    /// <summary>Null for corrupt or nonsensical records, which can't be safely restored.</summary>
    public static MicRestoreRecord? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<MicRestoreRecord>(json, JsonOptions) is { IsValid: true } record ? record : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
