using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Audio;

/// <summary>
/// The body of the elevated one-shot helper: turns on Listen for a mic, playing to a render endpoint. Unverified
/// on a real machine, including whether Windows applies the change without the mic being reopened.
/// </summary>
public static class ElevatedListenRepair
{
    /// <summary>Returns a <see cref="RepairExitCode"/>. Never throws.</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        if (RepairCommandLine.ParseListen(args) is not { } request)
            return (int)RepairExitCode.InvalidArguments;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var mic = enumerator.GetDevice(request.MicId);
            using var target = enumerator.GetDevice(request.TargetId);
            if (mic.DataFlow != DataFlow.Capture || target.DataFlow != DataFlow.Render)
                return (int)RepairExitCode.InvalidArguments;

            try
            {
                WriteToPropertyStore(mic, request.TargetId);
            }
            catch (Exception)
            {
                new WindowsAudioSystemWriter("").SetListen(request.MicId, request.TargetId);
            }

            return (int)RepairExitCode.Success;
        }
        catch (Exception)
        {
            return (int)RepairExitCode.Failed;
        }
    }

    // Target first, so Listen never briefly plays the mic somewhere else.
    private static void WriteToPropertyStore(MMDevice mic, string targetId)
    {
        mic.GetPropertyInformation(StorageAccessMode.ReadWrite);
        var store = mic.Properties;
        var target = new PropVariant { vt = (short)VarEnum.VT_LPWSTR, pointerValue = Marshal.StringToCoTaskMemUni(targetId) };
        try
        {
            store.SetValue(ListenProperties.Target, target);
        }
        finally
        {
            Marshal.FreeCoTaskMem(target.pointerValue);
        }

        store.SetValue(ListenProperties.Enabled, new PropVariant { vt = (short)VarEnum.VT_BOOL, boolVal = -1 });
        store.Commit();
    }
}
