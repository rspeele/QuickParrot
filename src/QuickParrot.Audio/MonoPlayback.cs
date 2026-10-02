namespace QuickParrot.Audio;

internal static class MonoPlayback
{
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(3);

    /// <summary>Plays mono samples on a device until they end (or a little longer); returns why it failed, or null.</summary>
    public static async Task<string?> TryPlayAsync(
        string deviceId, float[] samples, int sampleRate, CancellationToken cancellationToken)
    {
        try
        {
            await using var playback = WasapiOutput.Open(deviceId, new BufferSampleProvider(samples, sampleRate, 1));
            playback.Play();
            var duration = TimeSpan.FromSeconds(samples.Length / (double)sampleRate);
            return (await playback.Stopped.WaitAsync(duration + Slack, cancellationToken))?.Message;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return e.Message;
        }
    }
}
