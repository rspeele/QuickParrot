namespace QuickParrot.Core.Common;

/// <summary>
/// Coalesces a burst of signals into one callback, on a timer thread, once things settle. Each <see cref="Signal"/>
/// restarts the quiet period, so only the last one in a burst fires it.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private readonly Action _action;
    private readonly TimeSpan _delay;
    private readonly ITimer _timer;
    private readonly Lock _lock = new();
    private bool _disposed;

    public Debouncer(Action action, TimeSpan delay, TimeProvider time)
    {
        _action = action;
        _delay = delay;
        _timer = time.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Signal()
    {
        lock (_lock)
        {
            if (!_disposed)
                _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Cancels a pending callback; one already firing may still run.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }

    private void Fire()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
        }

        _action();
    }
}
