namespace QuickParrot.Core.Library;

/// <summary>
/// Coalesces a burst of filesystem signals into a single callback once things settle. Each <see cref="Signal"/>
/// restarts the quiet period, so only the last one in a burst fires it.
/// </summary>
public sealed class LibraryChangeDebouncer : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    private readonly Action _onChanged;
    private readonly TimeSpan _delay;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private ITimer? _timer;
    private bool _disposed;

    public LibraryChangeDebouncer(Action onChanged, TimeSpan delay, TimeProvider time)
    {
        _onChanged = onChanged;
        _delay = delay;
        _time = time;
    }

    /// <summary>Restarts the quiet period; <see cref="_onChanged"/> fires once it elapses without another signal.</summary>
    public void Signal()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _timer ??= _time.CreateTimer(_ => _onChanged(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
