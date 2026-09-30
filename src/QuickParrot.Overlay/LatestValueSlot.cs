namespace QuickParrot.Overlay;

/// <summary>
/// A lock-free single-value mailbox: producers overwrite, the consumer only ever sees the newest value.
/// <see cref="Set"/> returns true once per batch, when the consumer needs waking.
/// </summary>
internal sealed class LatestValueSlot<T>
    where T : class
{
    private T? _value;
    private int _wakePending;

    public bool Set(T? value)
    {
        Volatile.Write(ref _value, value);
        return RequestWake();
    }

    /// <summary>Asks for a wake-up without changing the value; true if the caller should deliver it.</summary>
    public bool RequestWake() => Interlocked.Exchange(ref _wakePending, 1) == 0;

    /// <summary>For when delivering the wake-up failed, so a later <see cref="Set"/> tries again.</summary>
    public void CancelWake() => Interlocked.Exchange(ref _wakePending, 0);

    // Clearing first (a full fence) means a Set racing with this either is seen here or wakes us again.
    public T? Take()
    {
        Interlocked.Exchange(ref _wakePending, 0);
        return Volatile.Read(ref _value);
    }
}
