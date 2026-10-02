namespace QuickParrot.Core.Diagnostics;

/// <summary>Mono float samples at a sample rate.</summary>
public sealed record MonoAudio(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);
}
