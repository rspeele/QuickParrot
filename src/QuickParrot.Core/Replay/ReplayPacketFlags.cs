namespace QuickParrot.Core.Replay;

/// <summary>Per-packet capture flags, mirroring WASAPI's AUDCLNT_BUFFERFLAGS_*.</summary>
[Flags]
public enum ReplayPacketFlags
{
    None = 0,

    /// <summary>Treat the packet as silence whatever its contents.</summary>
    Silent = 1,

    /// <summary>Audio was lost before this packet, so its timestamp is trusted even for small gaps.</summary>
    Discontinuity = 2,

    /// <summary>The timestamp is unreliable; the packet is appended straight after the previous one.</summary>
    TimestampError = 4,
}
