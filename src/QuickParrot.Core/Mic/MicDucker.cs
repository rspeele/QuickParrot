using QuickParrot.Core.Devices;
using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Mic;

/// <summary>
/// Mutes or attenuates the real mic while a clip plays, saving its original state first so a crash can be undone.
/// Call it from one thread at a time, except <see cref="EmergencyRestore"/>, which is callable from any thread.
/// </summary>
public sealed class MicDucker : IMicMuter
{
    private const string RetryLater = "QuickParrot will try again when it's reconnected.";

    private readonly IMicVolumeControl _control;
    private readonly ICaptureDeviceCatalog _devices;
    private readonly IMicRestoreStore _store;

    // Only ever contended by an emergency restore.
    private readonly Lock _lock = new();
    private MicDuckSettings _settings = MicDuckSettings.Off;
    private volatile MicRestoreRecord? _applied;
    private bool _clipDucked;
    private volatile bool _emergencyRestored;
    private string? _lastWarning;

    public MicDucker(IMicVolumeControl control, ICaptureDeviceCatalog devices, IMicRestoreStore store)
    {
        _control = control;
        _devices = devices;
        _store = store;
    }

    /// <summary>A user-facing problem with the mic. Repeats of the same message are suppressed.</summary>
    public event Action<string>? Warning;

    /// <summary>QuickParrot has the mic muted or turned down right now, or is waiting to undo that.</summary>
    public bool HasPendingRestore => _applied is not null;

    public static float AttenuatedVolume(float original, int percent) => Math.Clamp(original * percent / 100f, 0f, 1f);

    /// <summary>Turning ducking off also retries a pending restore, since no clip will.</summary>
    public void Configure(MicDuckSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            if (settings.Mode == MicDuckMode.Off)
                RetryRestore();
        }
    }

    /// <summary>
    /// Undoes a mic change left behind by a previous run that crashed mid-clip. Returns a user-facing message if
    /// there was one to undo, otherwise null. Never throws.
    /// </summary>
    public string? RestoreAfterCrash()
    {
        lock (_lock)
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

            const string uncleanExit = "QuickParrot didn't close cleanly during a clip";
            return TryRestore() switch
            {
                RestoreOutcome.Restored => $"{uncleanExit}, so {NameOf(record)} was restored to how it was.",
                RestoreOutcome.DeviceGone => $"{uncleanExit}, and {NameOf(record)} is gone, so it couldn't be restored.",
                _ => $"{uncleanExit} and couldn't restore {NameOf(record)} yet. {RetryLater}",
            };
        }
    }

    public void Mute()
    {
        lock (_lock)
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
    }

    public void Unmute()
    {
        lock (_lock)
        {
            _clipDucked = false;
            if (_applied is not { } record)
                return;

            try
            {
                if (TryRestore() == RestoreOutcome.Failed)
                    Warn($"Couldn't restore {NameOf(record)}. {RetryLater}");
            }
            catch (Exception e)
            {
                Warn($"Couldn't restore your microphone: {e.Message}");
            }
        }
    }

    /// <summary>Retries a restore that failed earlier, e.g. once its device is back. Leaves a clip's change alone.</summary>
    public void RetryRestore()
    {
        lock (_lock)
        {
            if (_clipDucked || _applied is not { } record)
                return;

            var outcome = TryRestore();
            if (outcome == RestoreOutcome.Restored)
                Warn($"{Capitalized(NameOf(record))} was restored to how it was.");
            else if (outcome == RestoreOutcome.DeviceGone)
                Warn($"{Capitalized(NameOf(record))} is gone, so it couldn't be restored.");
        }
    }

    /// <summary>
    /// For a crash: restores the mic from any thread, waiting at most <paramref name="lockTimeout"/> for a call in
    /// progress. If that call is stuck, the record is also kept for the next startup. Never throws.
    /// </summary>
    public void EmergencyRestore(TimeSpan lockTimeout)
    {
        var entered = false;
        try
        {
            entered = _lock.TryEnter(lockTimeout);
            _emergencyRestored = true;
            var record = _applied ?? (entered ? null : _store.Load());
            if (record is null)
                return;

            Apply(record);
            if (entered)
            {
                _applied = null;
                _store.Delete();
            }
        }
        catch (Exception)
        {
            // Best effort: the record stays for the next startup.
        }
        finally
        {
            if (entered)
                _lock.Exit();
        }
    }

    private void Duck()
    {
        var settings = _settings;
        if (settings.Mode == MicDuckMode.Off || _emergencyRestored)
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

        _clipDucked = true;

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

            Apply(record);
        }
        catch (Exception)
        {
            return RestoreOutcome.Failed;
        }

        Forget();
        return RestoreOutcome.Restored;
    }

    private void Apply(MicRestoreRecord record)
    {
        if (record.Mode == MicDuckMode.Mute)
            _control.SetMute(record.DeviceId, record.OriginalMuted);
        else
            _control.SetVolume(record.DeviceId, record.OriginalVolume);
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

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string NameOf(MicRestoreRecord record) =>
        string.IsNullOrEmpty(record.DeviceName) ? "your microphone" : $"your microphone ({record.DeviceName})";

    private enum RestoreOutcome
    {
        Restored,
        DeviceGone,
        Failed,
    }
}
