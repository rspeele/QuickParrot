using QuickParrot.Core.Devices;
using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Mic;

/// <summary>
/// Mutes or attenuates the real mic while a clip plays, saving its original state first so it can be restored
/// even after a crash. Not thread-safe: call <see cref="RestoreAfterCrash"/> before handing this to the engine,
/// which then makes every other call from its worker thread.
/// </summary>
public sealed class MicDucker : IMicMuter
{
    private readonly IMicVolumeControl _control;
    private readonly ICaptureDeviceCatalog _devices;
    private readonly IMicRestoreStore _store;
    private MicDuckSettings _settings = MicDuckSettings.Off;
    private MicRestoreRecord? _applied;
    private string? _lastWarning;

    public MicDucker(IMicVolumeControl control, ICaptureDeviceCatalog devices, IMicRestoreStore store)
    {
        _control = control;
        _devices = devices;
        _store = store;
    }

    /// <summary>A user-facing problem with the mic. Repeats of the same message are suppressed.</summary>
    public event Action<string>? Warning;

    public static float AttenuatedVolume(float original, int percent) => Math.Clamp(original * percent / 100f, 0f, 1f);

    public void Configure(MicDuckSettings settings) => _settings = settings;

    /// <summary>
    /// Undoes a mic change left behind by a previous run that crashed mid-clip. Returns a user-facing message if
    /// there was one to undo, otherwise null. Never throws.
    /// </summary>
    public string? RestoreAfterCrash()
    {
        try
        {
            _applied = _store.Load();
        }
        catch (Exception e)
        {
            return $"Couldn't check whether your microphone needs restoring: {e.Message}";
        }

        if (_applied is not { } record)
            return null;

        return TryRestore() switch
        {
            RestoreOutcome.Restored =>
                $"QuickParrot didn't close cleanly during a clip, so {NameOf(record)} was restored to how it was.",
            RestoreOutcome.DeviceGone =>
                $"QuickParrot didn't close cleanly during a clip, and {NameOf(record)} is gone, so it couldn't be restored.",
            _ => $"QuickParrot didn't close cleanly during a clip and couldn't restore {NameOf(record)} yet. "
                + "It'll try again before the next clip.",
        };
    }

    public void Mute()
    {
        try
        {
            Duck();
        }
        catch (Exception e)
        {
            Warn($"Couldn't change your microphone: {e.Message}");
        }
    }

    public void Unmute()
    {
        if (_applied is not { } record)
            return;

        try
        {
            if (TryRestore() == RestoreOutcome.Failed)
                Warn($"Couldn't restore {NameOf(record)}. QuickParrot will try again before the next clip.");
        }
        catch (Exception e)
        {
            Warn($"Couldn't restore your microphone: {e.Message}");
        }
    }

    private void Duck()
    {
        var settings = _settings;
        if (settings.Mode == MicDuckMode.Off)
            return;

        // A change that couldn't be undone earlier must be undone first, or its ducked level would be saved as original.
        if (_applied is { } leftover && TryRestore() == RestoreOutcome.Failed)
        {
            Warn($"{NameOf(leftover)} is still waiting to be restored, so it wasn't muted for this clip.");
            return;
        }

        var selection = MicDeviceSelector.Select(
            _devices.GetCaptureDevices(),
            settings.DeviceId,
            _devices.GetDefaultCaptureDeviceId(),
            _devices.GetDefaultCommunicationsCaptureDeviceId());
        if (selection.Device is not { } device)
        {
            Warn("No microphone was found to mute, so it's left as it is.");
            return;
        }

        var original = _control.Read(device.Id);
        var record = new MicRestoreRecord(device.Id, device.Name, settings.Mode, original.Muted, original.Volume);
        _store.Save(record);
        _applied = record;
        try
        {
            if (settings.Mode == MicDuckMode.Mute)
                _control.SetMute(device.Id, true);
            else
                _control.SetVolume(device.Id, AttenuatedVolume(original.Volume, settings.AttenuationPercent));
        }
        catch
        {
            TryRestore();
            throw;
        }

        if (selection.ConfiguredUnavailable)
            Warn($"Your chosen microphone isn't available, so {device.Name} was used instead.");
        else
            _lastWarning = null;
    }

    // Puts back only what was changed, then forgets the record.
    private RestoreOutcome TryRestore()
    {
        var record = _applied!;
        try
        {
            var device = _devices.GetCaptureDevices()
                .FirstOrDefault(d => string.Equals(d.Id, record.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (device is null)
            {
                Forget();
                return RestoreOutcome.DeviceGone;
            }

            if (!device.IsActive)
                return RestoreOutcome.Failed;

            if (record.Mode == MicDuckMode.Mute)
                _control.SetMute(record.DeviceId, record.OriginalMuted);
            else
                _control.SetVolume(record.DeviceId, record.OriginalVolume);
        }
        catch (Exception)
        {
            return RestoreOutcome.Failed;
        }

        Forget();
        return RestoreOutcome.Restored;
    }

    private void Forget()
    {
        _applied = null;
        try
        {
            _store.Delete();
        }
        catch (Exception e)
        {
            Warn($"Couldn't delete the saved microphone state: {e.Message}");
        }
    }

    private void Warn(string message)
    {
        if (message == _lastWarning)
            return;

        _lastWarning = message;
        try
        {
            Warning?.Invoke(message);
        }
        catch (Exception)
        {
            // A broken handler must not stop the mic being restored.
        }
    }

    private static string NameOf(MicRestoreRecord record) =>
        string.IsNullOrEmpty(record.DeviceName) ? "your microphone" : $"your microphone ({record.DeviceName})";

    private enum RestoreOutcome
    {
        Restored,
        DeviceGone,
        Failed,
    }
}
