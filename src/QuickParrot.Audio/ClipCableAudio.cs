using QuickParrot.Core.Diagnostics;
using QuickParrot.Core.Dsp;

namespace QuickParrot.Audio;

/// <summary>Decodes clips, records CABLE Output and plays recordings back for <see cref="ClipCableTest"/>.</summary>
public sealed class ClipCableAudio : IClipCableAudio
{
    public Task<MonoAudio> LoadClipAsync(string fullPath, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var clip = DecodedClip.Decode(fullPath, cancellationToken);
                return new MonoAudio(AudioDownmixer.ToMono(clip.Samples.Span, clip.Format.Channels), clip.Format.SampleRate);
            },
            cancellationToken);

    public ICableRecording StartRecording(string captureDeviceId, TimeSpan maxDuration) =>
        new Recording(LoopbackTesterCapture.Start(captureDeviceId, maxDuration));

    public Task<string?> PlayAsync(string renderDeviceId, MonoAudio audio, CancellationToken cancellationToken) =>
        Task.Run(
            () => MonoPlayback.TryPlayAsync(renderDeviceId, audio.Samples, audio.SampleRate, cancellationToken),
            cancellationToken);

    private sealed class Recording(LoopbackTesterCapture capture) : ICableRecording
    {
        public async Task<MonoAudio> StopAsync()
        {
            var (samples, sampleRate, error) = await capture.StopAsync();
            return error is null ? new MonoAudio(samples, sampleRate) : throw new InvalidOperationException(error);
        }

        public void Dispose() => capture.Dispose();
    }
}
