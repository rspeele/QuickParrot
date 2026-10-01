using QuickParrot.Core.Devices;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Playback;

namespace QuickParrot.Audio;

/// <summary>
/// Previews editor audio on the monitor device only (see <see cref="PreviewDeviceSelector"/>), at the monitor volume.
/// <paramref name="settings"/> supplies the current device choices each time preview starts.
/// </summary>
public sealed class EditorPreview(IAudioDeviceCatalog devices, Func<OutputSettings> settings) : IEditorPreview, IDisposable
{
    private EditorPreviewOutput? _current;
    private int _generation;

    public event Action<EditableAudio, Exception?>? Stopped;

    public EditableAudio? PlayingAudio => Volatile.Read(ref _current)?.Audio;

    public int? PositionFrame => Volatile.Read(ref _current)?.PositionFrame;

    public async Task PlayAsync(EditableAudio audio, int startFrame, int endFrame, float gain, CancellationToken cancellationToken)
    {
        Stop();
        var generation = _generation;
        var current = settings();
        var output = await Task.Run(() =>
        {
            var device = PreviewDeviceSelector.Select(
                devices.GetRenderDevices(), current.CableDeviceId, current.MonitorDeviceId, devices.GetDefaultRenderDeviceId());
            if (device.Device is null)
                throw new InvalidOperationException(device.Problem);

            return EditorPreviewOutput.Open(device.Device.Id, audio, startFrame, endFrame, gain * current.MonitorVolume);
        }, cancellationToken);

        if (generation != _generation || cancellationToken.IsCancellationRequested)
        {
            output.Dispose(); // stopped or replaced while the device was opening
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        output.Stopped.OnCompleted(error => OnStopped(output, error));
        Volatile.Write(ref _current, output);
        output.Play();
    }

    public void Stop()
    {
        _generation++;
        Interlocked.Exchange(ref _current, null)?.Dispose();
    }

    public void Dispose() => Stop();

    private void OnStopped(EditorPreviewOutput output, Exception? error)
    {
        if (Interlocked.CompareExchange(ref _current, null, output) != output)
            return;

        output.Dispose();
        Stopped?.Invoke(output.Audio, error);
    }
}
