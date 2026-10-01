namespace QuickParrot.Core.Settings;

/// <summary>
/// The UI's one copy of the engine's settings. Local changes show at once and go to the engine; engine updates that
/// predate the latest local change are skipped, so a dragged slider never jumps back. UI thread only.
/// </summary>
/// <param name="send">Queues a change on the engine, e.g. <c>QuickParrotEngine.UpdateSettings</c>.</param>
/// <param name="postToUi">Runs an action on the UI thread, in the order posted.</param>
public sealed class SettingsMirror(AppSettings initial, Action<Func<AppSettings, AppSettings>> send, Action<Action> postToUi)
{
    private int _sent;
    private int _acknowledged;

    public AppSettings Current { get; private set; } = initial;

    /// <summary>Raised with the previous and the new settings whenever <see cref="Current"/> changes.</summary>
    public event Action<AppSettings, AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        var updated = change(Current).Sanitized();
        if (updated.Equals(Current))
            return;

        // Posted before the engine publishes the result, so Receive knows which updates already include this change.
        var sequence = ++_sent;
        send(settings =>
        {
            postToUi(() => _acknowledged = sequence);
            return change(settings);
        });
        Set(updated);
    }

    /// <summary>Call on the UI thread with every settings value the engine publishes.</summary>
    public void Receive(AppSettings settings)
    {
        if (_acknowledged == _sent)
            Set(settings);
    }

    private void Set(AppSettings settings)
    {
        var old = Current;
        if (old.Equals(settings))
            return;

        Current = settings;
        Changed?.Invoke(old, settings);
    }
}
