using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Diagnostics;

/// <summary>
/// What the Diagnostics tests share: one runs at a time, chord playback is suppressed while one records the cable,
/// the running one can be cancelled, and whether the cable's devices are there. Not thread-safe; use from the UI.
/// </summary>
/// <param name="resolveSetup">Resolves the devices from the current settings and system.</param>
/// <param name="suppressPlayback">The engine's <c>SuppressPlayback</c>.</param>
/// <param name="stopPlayback">The engine's <c>Stop</c>.</param>
public sealed class CableTestRunner(Func<LoopbackTestSetup> resolveSetup, Action<bool> suppressPlayback, Action stopPlayback)
{
    private Run? _current;
    private bool? _setupReady;

    /// <summary>Raised when <see cref="IsBusy"/> or <see cref="IsSetupReady"/> changes.</summary>
    public event Action? Changed;

    public bool IsBusy => _current is not null;

    public bool IsSetupReady => _setupReady ??= Resolve().IsReady;

    /// <summary>Re-resolves <see cref="IsSetupReady"/>; call when the audio setup may have changed.</summary>
    public void Refresh()
    {
        var ready = Resolve().IsReady;
        if (ready == _setupReady)
            return;

        _setupReady = ready;
        Changed?.Invoke();
    }

    /// <summary>
    /// Starts a run, or returns null if one is already running or (when <paramref name="recordsCable"/>) the cable
    /// isn't ready. A run that records the cable stops and suppresses playback until it's disposed.
    /// </summary>
    public Run? TryStart(bool recordsCable)
    {
        if (_current is not null)
            return null;

        var setup = Resolve();
        if (recordsCable && !setup.IsReady)
            return null;

        if (recordsCable)
        {
            suppressPlayback(true); // first, so a chord queued meanwhile can't start a clip after the stop
            stopPlayback();
        }

        _current = new Run(this, setup, recordsCable);
        Changed?.Invoke();
        return _current;
    }

    public void Cancel() => _current?.Cancel();

    // Reading devices can fail mid-change; that's just "not ready" rather than an error to surface.
    private LoopbackTestSetup Resolve()
    {
        try
        {
            return resolveSetup();
        }
        catch (Exception)
        {
            return new LoopbackTestSetup(null, null, null);
        }
    }

    private void End(Run run)
    {
        if (_current != run)
            return;

        _current = null;
        if (run.RecordsCable)
            suppressPlayback(false);
        Changed?.Invoke();
    }

    public sealed class Run : IDisposable
    {
        private readonly CableTestRunner _owner;
        private readonly CancellationTokenSource _cts = new();

        internal Run(CableTestRunner owner, LoopbackTestSetup setup, bool recordsCable)
        {
            _owner = owner;
            Setup = setup;
            RecordsCable = recordsCable;
        }

        public LoopbackTestSetup Setup { get; }

        public bool RecordsCable { get; }

        public CancellationToken Token => _cts.Token;

        internal void Cancel() => _cts.Cancel();

        public void Dispose()
        {
            _owner.End(this);
            _cts.Dispose();
        }
    }
}
