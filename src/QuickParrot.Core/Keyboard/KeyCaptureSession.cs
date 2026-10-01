namespace QuickParrot.Core.Keyboard;

public enum KeyCaptureOutcome
{
    Captured,

    /// <summary>Esc, or <see cref="KeyCaptureSession.Cancel"/>; the status shown before capturing should come back.</summary>
    Cancelled,

    /// <summary>A newer capture took over; it owns the status line now.</summary>
    Superseded,

    /// <summary>The keyboard hook isn't running.</summary>
    Unavailable,
}

public readonly record struct KeyCaptureResult(KeyCaptureOutcome Outcome, ScanKey Key, string MessageBefore);

/// <summary>Captures one key at a time for rebinding: starting a new capture supersedes the one in progress.</summary>
public sealed class KeyCaptureSession(Func<CancellationToken, Task<ScanKey?>> captureNextKey)
{
    public const string Prompt = "Press a key… (Esc to cancel)";

    private CancellationTokenSource? _current;
    private string _messageBefore = "";

    /// <param name="messageBefore">The status message to restore on cancel; ignored when superseding a capture.</param>
    public async Task<KeyCaptureResult> CaptureAsync(string messageBefore)
    {
        if (_current is null)
            _messageBefore = messageBefore;

        // Replaced before cancelling, since the old capture may finish inline and must see it was superseded.
        var previous = _current;
        var cts = new CancellationTokenSource();
        _current = cts;
        previous?.Cancel();

        ScanKey? key = null;
        var unavailable = false;
        try
        {
            key = await captureNextKey(cts.Token);
        }
        catch (InvalidOperationException)
        {
            unavailable = true;
        }
        catch (OperationCanceledException)
        {
        }

        var superseded = _current != cts;
        if (!superseded)
            _current = null;
        cts.Dispose();

        var outcome = (superseded, unavailable, key) switch
        {
            (true, _, _) => KeyCaptureOutcome.Superseded,
            (_, true, _) => KeyCaptureOutcome.Unavailable,
            (_, _, null) => KeyCaptureOutcome.Cancelled,
            _ => KeyCaptureOutcome.Captured,
        };
        return new KeyCaptureResult(outcome, key ?? default, _messageBefore);
    }

    /// <summary>Cancels the capture in progress, if any, e.g. because the window lost focus.</summary>
    public void Cancel() => _current?.Cancel();
}
