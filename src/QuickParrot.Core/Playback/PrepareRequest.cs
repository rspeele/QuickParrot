namespace QuickParrot.Core.Playback;

/// <summary>
/// One in-flight <see cref="IClipPlayer.PrepareAsync"/>. Completion arrives on any thread and cancellation on the
/// controller's; whichever comes second disposes an unwanted clip, so none leaks even if a dispatch is dropped.
/// </summary>
internal sealed class PrepareRequest(string fullPath)
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task<IPreparedClip>? _result;
    private bool _cancelled;

    public string FullPath => fullPath;

    public CancellationToken Token => _cancellation.Token;

    /// <summary>Stores the result; returns false (having disposed any clip) if the request was already cancelled.</summary>
    public bool TryComplete(Task<IPreparedClip> result)
    {
        lock (_lock)
        {
            if (!_cancelled)
            {
                _result = result;
                return true;
            }
        }

        Discard(result);
        return false;
    }

    /// <summary>Takes the completed result for use; null if cancelled or not yet complete.</summary>
    public Task<IPreparedClip>? Take()
    {
        lock (_lock)
        {
            var result = _cancelled ? null : _result;
            _result = null;
            return result;
        }
    }

    public void Cancel()
    {
        Task<IPreparedClip>? orphan;
        lock (_lock)
        {
            if (_cancelled)
                return;

            _cancelled = true;
            orphan = _result;
            _result = null;
        }

        _cancellation.Cancel();
        if (orphan is not null)
            Discard(orphan);
    }

    private static void Discard(Task<IPreparedClip> result)
    {
        if (result.IsCompletedSuccessfully)
            result.Result.Dispose();
        else
            _ = result.Exception; // observed, so it isn't reported as unobserved
    }
}
