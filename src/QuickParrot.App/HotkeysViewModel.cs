using QuickParrot.App.Mvvm;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

public sealed record MouseButtonChoice(PushToTalkMouseButton Button, string Label);

/// <summary>Rebinding hotkeys, validated against the current settings.</summary>
public sealed class HotkeysViewModel : ObservableObject
{
    private static readonly (string, Func<AppSettings, object?>)[] Properties =
    [
        (nameof(ChordKeyDisplay), s => s.ChordKey),
        (nameof(GrabHotkeyDisplay), s => s.ChordKey),
        (nameof(SaveNavigationKeyDisplay), s => s.SaveNavigationKey),
        (nameof(SearchKeyDisplay), s => s.SearchKey),
        (nameof(FragmentsKeyDisplay), s => s.FragmentsKey),
        (nameof(PushToTalkBindingDisplay), s => s.PushToTalkBinding),
        (nameof(SelectedPushToTalkMouseButton), s => s.PushToTalkBinding),
    ];

    private readonly SettingsMirror _settings;
    private readonly StatusViewModel _status;
    private readonly KeyCaptureSession _capture;

    /// <param name="captureNextKey">The keyboard hook's next-key capture; throws <see cref="InvalidOperationException"/> when it isn't running.</param>
    public HotkeysViewModel(
        SettingsMirror settings, StatusViewModel status, Func<CancellationToken, Task<ScanKey?>> captureNextKey)
    {
        _settings = settings;
        _status = status;
        _capture = new KeyCaptureSession(captureNextKey);
        _settings.Changed += (old, now) => OnPropertiesChanged(old, now, Properties);
    }

    public string ChordKeyDisplay => _settings.Current.ChordKey.ToString();

    public string GrabHotkeyDisplay => $"{ChordKeyDisplay}+Enter";

    public string SaveNavigationKeyDisplay => _settings.Current.SaveNavigationKey.ToString();

    public string SearchKeyDisplay => _settings.Current.SearchKey.ToString();

    public string FragmentsKeyDisplay => _settings.Current.FragmentsKey.ToString();

    public string PushToTalkBindingDisplay => _settings.Current.PushToTalkBinding.ToString();

    public IReadOnlyList<MouseButtonChoice> PushToTalkMouseButtonChoices { get; } =
        Enum.GetValues<PushToTalkMouseButton>()
            .Select(button => new MouseButtonChoice(button, PushToTalkBinding.FromMouse(button).ToString()))
            .ToList();

    /// <summary>Null when the current binding is a keyboard key rather than a mouse button.</summary>
    public PushToTalkMouseButton? SelectedPushToTalkMouseButton
    {
        get => _settings.Current.PushToTalkBinding.MouseButton;
        set
        {
            if (value is not null)
                TryApplyPushToTalkBinding(PushToTalkBinding.FromMouse(value.Value));
        }
    }

    /// <summary>Waits for the next key press and, if valid, makes it the chord key.</summary>
    public Task ChangeChordKeyAsync() => CaptureKeyAsync("the chord key", key =>
    {
        if (_settings.Current.WithChordKey(key).Error is { } error)
        {
            _status.Report(error);
            return;
        }

        // Re-validated against the engine's settings, in case they moved on since.
        var before = _settings.Current.ChordKey;
        _settings.Update(s => s.WithChordKey(key).Settings);
        _status.Report(before == key ? $"The chord key is already {key}." : $"Chord key changed to {key}.");
    });

    /// <summary>Waits for the next key press and, if valid, makes it the push-to-talk binding.</summary>
    public Task ChangePushToTalkKeyAsync() => CaptureKeyAsync("push-to-talk", key =>
        TryApplyPushToTalkBinding(PushToTalkBinding.FromKey(key)));

    public Task ChangeSaveNavigationKeyAsync() => CaptureKeyAsync("the save navigation key", key =>
    {
        if (_settings.Current.WithSaveNavigationKey(key).Error is { } error)
        {
            _status.Report(error);
            return;
        }

        var before = _settings.Current.SaveNavigationKey;
        _settings.Update(s => s.WithSaveNavigationKey(key).Settings);
        _status.Report(before == key ? $"Save navigation is already {key}." : $"Save navigation key changed to {key}.");
    });

    public Task ChangeSearchKeyAsync() => CaptureKeyAsync("the search key", key =>
    {
        if (_settings.Current.WithSearchKey(key).Error is { } error)
        {
            _status.Report(error);
            return;
        }
        var before = _settings.Current.SearchKey;
        _settings.Update(s => s.WithSearchKey(key).Settings);
        _status.Report(before == key ? $"Search is already {key}." : $"Search key changed to {key}.");
    });

    public Task ChangeFragmentsKeyAsync() => CaptureKeyAsync("the fragments key", key =>
    {
        if (_settings.Current.WithFragmentsKey(key).Error is { } error)
        {
            _status.Report(error);
            return;
        }
        var before = _settings.Current.FragmentsKey;
        _settings.Update(s => s.WithFragmentsKey(key).Settings);
        _status.Report(before == key ? $"Fragments is already {key}." : $"Fragments key changed to {key}.");
    });

    /// <summary>Cancels an in-progress key capture, e.g. because the window lost focus.</summary>
    public void CancelKeyCapture() => _capture.Cancel();

    private async Task CaptureKeyAsync(string subject, Action<ScanKey> onCaptured)
    {
        var messageBefore = _status.Message;
        _status.Report(KeyCaptureSession.Prompt);
        var result = await _capture.CaptureAsync(messageBefore);
        switch (result.Outcome)
        {
            case KeyCaptureOutcome.Captured:
                onCaptured(result.Key);
                break;
            case KeyCaptureOutcome.Cancelled:
                _status.Restore(KeyCaptureSession.Prompt, result.MessageBefore);
                break;
            case KeyCaptureOutcome.Unavailable:
                _status.Report($"Hotkeys aren't running, so {subject} can't be changed.");
                break;
        }
    }

    private void TryApplyPushToTalkBinding(PushToTalkBinding binding)
    {
        if (_settings.Current.WithPushToTalkBinding(binding).Error is { } error)
        {
            _status.Report(error);
            return;
        }

        var before = _settings.Current.PushToTalkBinding;
        _settings.Update(s => s.WithPushToTalkBinding(binding).Settings);
        _status.Report(before == binding ? $"Push-to-talk is already {binding}." : $"Push-to-talk changed to {binding}.");
    }
}
