namespace QuickParrot.Core.Mic;

/// <summary>How the mic was before QuickParrot changed it, persisted so a crash mid-clip can be undone.</summary>
public sealed record MicRestoreRecord(
    string DeviceId, string DeviceName, MicDuckMode Mode, bool OriginalMuted, float OriginalVolume)
{
    public bool IsValid =>
        !string.IsNullOrEmpty(DeviceId)
        && Mode is MicDuckMode.Mute or MicDuckMode.Attenuate
        && float.IsFinite(OriginalVolume) && OriginalVolume is >= 0f and <= 1f;
}

public interface IMicRestoreStore
{
    /// <summary>Null if there's no record, or it's unreadable.</summary>
    MicRestoreRecord? Load();

    void Save(MicRestoreRecord record);

    void Delete();
}
