namespace QuickParrot.Core.Editing;

/// <param name="Name">The suggested clip name, or null if there's none.</param>
/// <param name="Error">Why there's no name (no speech, a network or service failure...), if there's a reason to give.</param>
public sealed record NameSuggestion(string? Name, string? Error = null);

/// <summary>Runs a (slow, possibly remote) clip namer on request, cancelling any suggestion still in flight.</summary>
public sealed class NameSuggester : IDisposable
{
    private readonly Func<EditableAudio, CancellationToken, Task<NameSuggestion>> _suggest;
    private CancellationTokenSource? _pending;

    public NameSuggester(Func<EditableAudio, CancellationToken, Task<NameSuggestion>> suggest)
    {
        _suggest = suggest;
    }

    public bool IsBusy => _pending is not null;

    /// <summary>
    /// Suggests immediately, with the name sanitized; completes with null if cancelled (e.g. by <see cref="Dispose"/>)
    /// before finishing.
    /// </summary>
    public async Task<NameSuggestion?> SuggestNowAsync(EditableAudio selection)
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;
        try
        {
            var suggestion = await _suggest(selection, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var name = suggestion.Name;
            return suggestion with { Name = string.IsNullOrWhiteSpace(name) ? null : ClipFileNames.Sanitize(name) };
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
