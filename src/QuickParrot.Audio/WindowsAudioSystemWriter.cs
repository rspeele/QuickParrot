using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Audio;

/// <summary>
/// Changes Windows audio settings for <see cref="AudioSetupRepairer"/>. None of these writes has been exercised
/// on a real machine yet.
/// </summary>
/// <param name="helperExePath">This app's exe, relaunched elevated with <see cref="RepairCommandLine"/> arguments.</param>
public sealed class WindowsAudioSystemWriter(string helperExePath) : IAudioSystemWriter
{
    private const int ErrorCancelled = 1223;

    public void SetDefaultDevice(string deviceId, DeviceRoles role) =>
        PolicyConfig.SetDefaultEndpoint(deviceId, role switch
        {
            DeviceRoles.Console => EndpointRole.Console,
            DeviceRoles.Multimedia => EndpointRole.Multimedia,
            DeviceRoles.Communications => EndpointRole.Communications,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        });

    // Goes through the audio service, like the Sound control panel, so it may work without elevation.
    public void SetListen(string micId, string targetId)
    {
        var target = NativePropVariant.FromString(targetId);
        try
        {
            PolicyConfig.SetPropertyValue(micId, ListenProperties.ToNative(ListenProperties.Target), target);
        }
        finally
        {
            Marshal.FreeCoTaskMem(target.Pointer);
        }

        PolicyConfig.SetPropertyValue(micId, ListenProperties.ToNative(ListenProperties.Enabled), NativePropVariant.FromBool(true));
    }

    public async Task<ElevatedRunResult> SetListenElevatedAsync(string micId, string targetId, TimeSpan timeout)
    {
        Process? process;
        try
        {
            var arguments = RepairCommandLine.ToCommandLine(RepairCommandLine.ListenArguments(micId, targetId));
            process = Process.Start(new ProcessStartInfo(helperExePath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            return new ElevatedRunResult(ElevatedRunStatus.Declined);
        }
        catch (Exception e)
        {
            return new ElevatedRunResult(ElevatedRunStatus.CouldNotStart, Error: e.Message);
        }

        if (process is null)
            return new ElevatedRunResult(ElevatedRunStatus.CouldNotStart);

        using (process)
        {
            using var deadline = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                return new ElevatedRunResult(ElevatedRunStatus.Completed, process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                return new ElevatedRunResult(ElevatedRunStatus.TimedOut);
            }
            catch (Exception e)
            {
                return new ElevatedRunResult(ElevatedRunStatus.CouldNotStart, Error: e.Message);
            }
        }
    }

    public void SetLevel(string deviceId, bool muted, float? volume)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        if (device.State != DeviceState.Active)
            throw new InvalidOperationException($"{device.FriendlyName} isn't connected.");

        var endpoint = device.AudioEndpointVolume;
        endpoint.Mute = muted;
        if (volume is { } level)
            endpoint.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
    }

    public void SetCommunicationsDucking(CommunicationsDucking value) => CommunicationsDuckingRegistry.Write(value);

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
