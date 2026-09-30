namespace QuickParrot.Core.Settings;

public interface ISettingsStore
{
    /// <summary>Never throws; problems worth telling the user about come back as a warning.</summary>
    SettingsLoadResult Load();

    void Save(AppSettings settings);
}

public sealed record SettingsLoadResult(AppSettings Settings, string? Warning = null);
