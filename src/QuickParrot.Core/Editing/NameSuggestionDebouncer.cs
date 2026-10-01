namespace QuickParrot.Core.Editing;

/// <param name="Name">The sanitized suggestion, or null if the namer had none or failed.</param>
public sealed record NameSuggestion(string? Name, string? Error = null);

/// <summary>Runs a (slow, possibly remote) clip namer on request, cancelling any suggestion still in flight.</summary>
public sealed class NameSuggestionDebouncer : IDisposable
{
    private readonly Func<EditableAudio, CancellationToken, Task<string?>> _suggest;
    private CancellationTokenSource? _pending;

    public NameSuggestionDebouncer(Func<EditableAudio, CancellationToken, Task<string?>> suggest)
    {
        _suggest = suggest;
    }

    public bool IsBusy => _pending is not null;

    /// <summary>Suggests immediately; completes with null if cancelled (e.g. by <see cref="Dispose"/>) before finishing.</summary>
    public async Task<NameSuggestion?> SuggestNowAsync(EditableAudio selection)
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;
        try
        {
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

    public void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
    }

    public void Dispose() => Cancel();
}
