namespace QuickParrot.Core.Editing;

/// <param name="Name">The sanitized suggestion, or null if the namer had none or failed.</param>
public sealed record NameSuggestion(string? Name, string? Error = null);

/// <summary>
/// Runs a (slow, possibly remote) clip namer at most once per settled selection: each request cancels the previous one,
/// and automatic requests wait out a quiet period first.
/// </summary>
public sealed class NameSuggestionDebouncer : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(800);

    private readonly Func<EditableAudio, CancellationToken, Task<string?>> _suggest;
    private readonly TimeSpan _delay;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _pending;

    public NameSuggestionDebouncer(Func<EditableAudio, CancellationToken, Task<string?>> suggest, TimeSpan delay, TimeProvider time)
    {
        _suggest = suggest;
        _delay = delay;
        _time = time;
    }

    public bool IsBusy => _pending is not null;

    /// <summary>Suggests after the quiet period; completes with null if superseded or cancelled before finishing.</summary>
    public Task<NameSuggestion?> RequestAsync(EditableAudio selection) => RunAsync(selection, _delay);

    public Task<NameSuggestion?> SuggestNowAsync(EditableAudio selection) => RunAsync(selection, TimeSpan.Zero);

    public void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
    }

    public void Dispose() => Cancel();

    private async Task<NameSuggestion?> RunAsync(EditableAudio selection, TimeSpan delay)
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _time, cts.Token);

            var name = await _suggest(selection, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            return new NameSuggestion(string.IsNullOrWhiteSpace(name) ? null : ClipFileNames.Sanitize(name));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            return cts.IsCancellationRequested ? null : new NameSuggestion(null, e.Message);
        }
        finally
        {
            if (_pending == cts)
                _pending = null;
            cts.Dispose();
        }
    }
}
