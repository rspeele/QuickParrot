using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace QuickParrot.Audio;

/// <summary>Turns on a mic's "Listen to this device", playing to a given render endpoint.</summary>
internal static class ListenWriter
{
    /// <summary>Goes through the audio service, like the Sound control panel, so it may work without elevation.</summary>
    public static void ViaAudioService(string micId, string targetId) =>
        Write(targetId, (key, value) => PolicyConfig.SetPropertyValue(micId, ListenProperties.ToNative(key), value));

    /// <summary>Writes the mic's property store directly, which needs elevation.</summary>
    public static void ViaPropertyStore(MMDevice mic, string targetId)
    {
        mic.GetPropertyInformation(StorageAccessMode.ReadWrite);
        var store = mic.Properties;
        Write(targetId, (key, value) => store.SetValue(key, ToNAudio(value)));
        store.Commit();
    }

    // Target first, so Listen never briefly plays the mic somewhere else.
    private static void Write(string targetId, Action<PropertyKey, NativePropVariant> set)
    {
        var target = NativePropVariant.FromString(targetId);
        try
        {
            set(ListenProperties.Target, target);
        }
        finally
        {
            Marshal.FreeCoTaskMem(target.Pointer);
        }

        set(ListenProperties.Enabled, NativePropVariant.FromBool(true));
    }

    private static PropVariant ToNAudio(NativePropVariant value) => value.VarType == NativePropVariant.VtBool
        ? new PropVariant { vt = (short)value.VarType, boolVal = value.BoolValue }
        : new PropVariant { vt = (short)value.VarType, pointerValue = value.Pointer };
}
