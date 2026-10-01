using NAudio.CoreAudioApi;
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

            // Goes through the audio service first, like the Sound control panel, since it's more likely to apply live.
            try
            {
                ListenWriter.ViaAudioService(request.MicId, request.TargetId);
            }
            catch (Exception)
            {
                ListenWriter.ViaPropertyStore(mic, request.TargetId);
            }

            return (int)RepairExitCode.Success;
        }
        catch (Exception)
        {
            return (int)RepairExitCode.Failed;
        }
    }
}
