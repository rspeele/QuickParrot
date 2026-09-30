namespace QuickParrot.Core.Mic;

/// <summary>What happens to the real microphone while a clip plays.</summary>
public enum MicDuckMode
{
    Off,
    Mute,
    Attenuate,
}

/// <param name="AttenuationPercent">In <see cref="MicDuckMode.Attenuate"/>, the mic volume drops to this percentage of
/// its original level.</param>
/// <param name="DeviceId">Null auto-detects the real mic.</param>
public sealed record MicDuckSettings(MicDuckMode Mode, int AttenuationPercent, string? DeviceId)
{
    public const int DefaultAttenuationPercent = 20;

    public static MicDuckSettings Off { get; } = new(MicDuckMode.Off, DefaultAttenuationPercent, null);
}
