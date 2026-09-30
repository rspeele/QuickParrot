using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Diagnostics;

/// <summary>Windows' "When Windows detects communications activity" choice (UserDuckingPreference).</summary>
public enum CommunicationsDucking
{
    MuteOthers = 0,
    ReduceBy80Percent = 1,
    ReduceBy50Percent = 2,
    DoNothing = 3,
}

public static class CommunicationsDuckingPreference
{
    /// <summary>The registry value, where Windows' default (reduce by 80%) is stored as no value at all.</summary>
    public static CommunicationsDucking? FromRegistryValue(object? value) => value switch
    {
        null => CommunicationsDucking.ReduceBy80Percent,
        int raw when Enum.IsDefined((CommunicationsDucking)raw) => (CommunicationsDucking)raw,
        _ => null,
    };
}

/// <summary>The Windows default roles an endpoint can hold.</summary>
[Flags]
public enum DeviceRoles
{
    None = 0,
    Console = 1,
    Multimedia = 2,
    Communications = 4,
    All = Console | Multimedia | Communications,
}

/// <summary>An endpoint's master mute and volume (0 to 1).</summary>
public sealed record EndpointLevel(bool Muted, float Volume);

/// <param name="TargetId">The render endpoint Listen plays to; null means the Windows default playback device.</param>
public sealed record ListenSetting(bool Enabled, string? TargetId)
{
    /// <summary>From the raw endpoint properties; a missing enabled flag means Listen was never turned on.</summary>
    public static ListenSetting FromProperties(object? enabled, object? target) =>
        new(enabled is true, target is string { Length: > 0 } id ? id : null);
}

/// <summary>Default endpoint IDs per role. A null ID means Windows has no default for that role.</summary>
public sealed record DefaultEndpoints(
    string? RenderConsole,
    string? RenderMultimedia,
    string? RenderCommunications,
    string? CaptureConsole,
    string? CaptureMultimedia,
    string? CaptureCommunications)
{
    public static DefaultEndpoints None { get; } = new(null, null, null, null, null, null);

    public string? Render(DeviceRoles role) => role switch
    {
        DeviceRoles.Console => RenderConsole,
        DeviceRoles.Multimedia => RenderMultimedia,
        DeviceRoles.Communications => RenderCommunications,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public string? Capture(DeviceRoles role) => role switch
    {
        DeviceRoles.Console => CaptureConsole,
        DeviceRoles.Multimedia => CaptureMultimedia,
        DeviceRoles.Communications => CaptureCommunications,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

/// <summary>The settings the diagnoser needs; null IDs mean auto-detect.</summary>
public sealed record ConfiguredDevices(string? CableId, string? MonitorId, string? MicId)
{
    public static ConfiguredDevices Auto { get; } = new(null, null, null);
}

/// <summary>
/// Everything the diagnoser looks at, read in one go. Anything that couldn't be read is null or missing from its
/// dictionary, and the checks that depend on it are skipped.
/// </summary>
public sealed record AudioSetupSnapshot
{
    /// <summary>False if the device lists couldn't be read at all; they are then empty.</summary>
    public bool DevicesReadable { get; init; } = true;

    public IReadOnlyList<AudioDeviceInfo> RenderDevices { get; init; } = [];

    public IReadOnlyList<CaptureDeviceInfo> CaptureDevices { get; init; } = [];

    /// <summary>Null if the defaults couldn't be read.</summary>
    public DefaultEndpoints? Defaults { get; init; }

    /// <summary>By capture device ID (case-insensitive); a missing entry means unknown.</summary>
    public IReadOnlyDictionary<string, ListenSetting> Listen { get; init; } = EmptyById<ListenSetting>();

    /// <summary>By device ID (case-insensitive); a missing entry means unknown.</summary>
    public IReadOnlyDictionary<string, EndpointLevel> Levels { get; init; } = EmptyById<EndpointLevel>();

    /// <summary>QuickParrot itself has the mic muted or turned down, e.g. mid-clip.</summary>
    public bool MicRestorePending { get; init; }

    /// <summary>Null if unknown or an unrecognised value.</summary>
    public CommunicationsDucking? CommunicationsDucking { get; init; }

    public ConfiguredDevices Configured { get; init; } = ConfiguredDevices.Auto;

    public static Dictionary<string, T> EmptyById<T>() => new(StringComparer.OrdinalIgnoreCase);
}
