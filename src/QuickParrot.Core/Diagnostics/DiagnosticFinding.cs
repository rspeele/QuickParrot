namespace QuickParrot.Core.Diagnostics;

/// <summary>Declared most severe first, so findings sort by it.</summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
    Advisory,
}

public enum FixKind
{
    /// <summary>Opens <see cref="DiagnosticFix.CableDownloadUrl"/> in the browser.</summary>
    OpenCableDownloadPage,

    /// <summary>Turns on Listen for <see cref="DiagnosticFix.DeviceId"/>, playing to <see cref="DiagnosticFix.TargetDeviceId"/>.</summary>
    EnableListen,

    /// <summary>Makes <see cref="DiagnosticFix.DeviceId"/> the default playback device for <see cref="DiagnosticFix.Roles"/>.</summary>
    SetDefaultPlayback,

    /// <summary>Makes <see cref="DiagnosticFix.DeviceId"/> the default recording device for <see cref="DiagnosticFix.Roles"/>.</summary>
    SetDefaultRecording,

    /// <summary>Unmutes <see cref="DiagnosticFix.DeviceId"/> and sets it to full volume.</summary>
    RestoreCableLevel,

    /// <summary>Unmutes <see cref="DiagnosticFix.DeviceId"/>, leaving its volume alone.</summary>
    UnmuteMic,

    /// <summary>Sets Windows communications ducking to "Do nothing".</summary>
    DisableCommunicationsDucking,
}

/// <param name="Label">Button text.</param>
public sealed record DiagnosticFix(
    FixKind Kind,
    string Label,
    string? DeviceId = null,
    string? TargetDeviceId = null,
    DeviceRoles Roles = DeviceRoles.None)
{
    public const string CableDownloadUrl = "https://vb-audio.com/Cable/";
}

/// <param name="Id">Stable across runs, e.g. for tests or remembering a dismissal.</param>
public sealed record DiagnosticFinding(
    string Id, DiagnosticSeverity Severity, string Title, string Explanation, DiagnosticFix? Fix = null);
