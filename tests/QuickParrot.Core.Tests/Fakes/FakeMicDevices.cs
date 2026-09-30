using QuickParrot.Core.Devices;
using QuickParrot.Core.Mic;

namespace QuickParrot.Core.Tests.Fakes;

public sealed class FakeCaptureDeviceCatalog : ICaptureDeviceCatalog
{
    public List<CaptureDeviceInfo> Devices { get; } = [];

    public string? DefaultId { get; set; }

    public string? CommunicationsId { get; set; }

    public IReadOnlyList<CaptureDeviceInfo> GetCaptureDevices() => Devices.ToList();

    public string? GetDefaultCaptureDeviceId() => DefaultId;

    public string? GetDefaultCommunicationsCaptureDeviceId() => CommunicationsId;

    public void SetState(string id, AudioDeviceState state)
    {
        var index = Devices.FindIndex(d => d.Id == id);
        Devices[index] = Devices[index] with { State = state };
    }
}

// Logs changes (not reads) into a shared log so tests can check they happen after the record is saved.
public sealed class FakeMicVolumeControl(List<string> log) : IMicVolumeControl
{
    public Dictionary<string, MicLevel> Levels { get; } = [];

    public bool ThrowOnRead { get; set; }

    public bool ThrowOnChange { get; set; }

    public MicLevel Read(string deviceId) =>
        ThrowOnRead ? throw new InvalidOperationException("read failed") : Levels[deviceId];

    public void SetMute(string deviceId, bool muted)
    {
        log.Add($"mute:{deviceId}={muted}");
        if (ThrowOnChange)
            throw new InvalidOperationException("change failed");

        Levels[deviceId] = Levels[deviceId] with { Muted = muted };
    }

    public void SetVolume(string deviceId, float volume)
    {
        log.Add($"volume:{deviceId}={volume}");
        if (ThrowOnChange)
            throw new InvalidOperationException("change failed");

        Levels[deviceId] = Levels[deviceId] with { Volume = volume };
    }
}

public sealed class FakeMicRestoreStore(List<string> log) : IMicRestoreStore
{
    public MicRestoreRecord? Record { get; set; }

    public bool ThrowOnSave { get; set; }

    public MicRestoreRecord? Load() => Record;

    public void Save(MicRestoreRecord record)
    {
        if (ThrowOnSave)
            throw new IOException("disk full");

        log.Add($"save:{record.DeviceId}");
        Record = record;
    }

    public void Delete()
    {
        log.Add("delete");
        Record = null;
    }
}
