namespace QuickParrot.Core.Naming;

public interface IClipNamer
{
    /// <summary>
    /// Transcribes and suggests a name for a clip. Never throws for network/HTTP/timeout/cancellation problems:
    /// those come back as a suggestion with a null <c>Name</c> and a human-readable <c>ErrorMessage</c>. Only
    /// programmer errors (e.g. invalid <paramref name="sampleRate"/> or <paramref name="channels"/>) throw.
    /// </summary>
    Task<ClipNameSuggestion?> SuggestAsync(
        ReadOnlyMemory<float> interleaved, int sampleRate, int channels, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="Transcript"/> is the speech-to-text result (possibly empty). <paramref name="Name"/> is the
/// suggested clip name, or null if none could be produced. <paramref name="ErrorMessage"/> is set whenever
/// <paramref name="Name"/> is null, explaining why (no speech detected, a network/HTTP failure, etc.).
/// </summary>
public sealed record ClipNameSuggestion(string Transcript, string? Name, string? ErrorMessage);

public sealed record ConnectionTestResult(bool Success, string Message);
