using NAudio.CoreAudioApi;

namespace QuickParrot.Audio;

/// <summary>A capture endpoint's "Listen to this device" properties.</summary>
internal static class ListenProperties
{
    private static readonly Guid SetId = new("24dbb0fc-9311-4b3d-9cf0-18ff155639d4");

    /// <summary>The playback target's endpoint ID (VT_LPWSTR); absent or empty means the default playback device.</summary>
    public static readonly PropertyKey Target = new(SetId, 0);

    /// <summary>VT_BOOL.</summary>
    public static readonly PropertyKey Enabled = new(SetId, 1);

    public static NativePropertyKey ToNative(PropertyKey key) => new(key.formatId, key.propertyId);

    public static bool IsListenKey(PropertyKey key) => key.formatId == SetId;
}
