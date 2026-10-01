using System.ComponentModel;
using System.Diagnostics;
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

    public void SetListen(string micId, string targetId) => ListenWriter.ViaAudioService(micId, targetId);

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

    public void SetLevel(string deviceId, bool muted, float? volume) => EndpointVolumes.Set(deviceId, muted, volume);

    public void SetCommunicationsDucking(CommunicationsDucking value) => CommunicationsDuckingRegistry.Write(value);

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
