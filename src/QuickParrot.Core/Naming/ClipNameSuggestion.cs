namespace QuickParrot.Core.Naming;

/// <summary>
/// <paramref name="Transcript"/> is the speech-to-text result (possibly empty). <paramref name="Name"/> is the
/// suggested clip name, or null if none could be produced. <paramref name="ErrorMessage"/> is set whenever
/// <paramref name="Name"/> is null, explaining why (no speech detected, a network/HTTP failure, etc.).
/// </summary>
public sealed record ClipNameSuggestion(string Transcript, string? Name, string? ErrorMessage);

public sealed record ConnectionTestResult(bool Success, string Message);
