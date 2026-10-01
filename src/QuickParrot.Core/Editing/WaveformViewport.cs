namespace QuickParrot.Core.Editing;

/// <summary>Maps frames to horizontal positions for a waveform of a given width, with zoom and scroll. Immutable.</summary>
public sealed record WaveformViewport
{
    /// <summary>The closest zoom: a few pixels per frame, enough to see individual samples.</summary>
    public const double MinFramesPerPixel = 1 / 8.0;

    public WaveformViewport(int totalFrames, double width = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalFrames, 0);
        TotalFrames = totalFrames;
        Width = Math.Max(1, width);
        FramesPerPixel = MaxFramesPerPixel;
    }

    public int TotalFrames { get; }

    public double Width { get; private init; }

    public double FirstFrame { get; private init; }

    public double FramesPerPixel { get; private init; }

    public double MaxFramesPerPixel => Math.Max(MinFramesPerPixel, TotalFrames / Width);

    public double VisibleFrames => Width * FramesPerPixel;

    public double MaxFirstFrame => Math.Max(0, TotalFrames - VisibleFrames);

    public bool IsZoomedIn => FramesPerPixel < MaxFramesPerPixel;

    private bool FitsAll { get; init; } = true;

    public double FrameAt(double x) => FirstFrame + x * FramesPerPixel;

    public double XAt(double frame) => (frame - FirstFrame) / FramesPerPixel;

    /// <summary>Resizes, keeping the zoom (or staying fully zoomed out) and the left edge where possible.</summary>
    public WaveformViewport WithWidth(double width)
    {
        var resized = this with { Width = Math.Max(1, width) };
        var framesPerPixel = FitsAll
            ? resized.MaxFramesPerPixel
            : Math.Clamp(FramesPerPixel, MinFramesPerPixel, resized.MaxFramesPerPixel);
        return (resized with { FramesPerPixel = framesPerPixel }).ScrollTo(FirstFrame);
    }

    /// <summary>Zooms by <paramref name="factor"/> (&gt;1 zooms in) keeping the frame under <paramref name="x"/> in place.</summary>
    public WaveformViewport ZoomAround(double x, double factor)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor));

        var anchor = FrameAt(x);
        var framesPerPixel = Math.Clamp(FramesPerPixel / factor, MinFramesPerPixel, MaxFramesPerPixel);
        var zoomed = this with { FramesPerPixel = framesPerPixel, FitsAll = framesPerPixel >= MaxFramesPerPixel };
        return zoomed.ScrollTo(anchor - x * framesPerPixel);
    }

    public WaveformViewport ShowAll() => this with { FitsAll = true, FramesPerPixel = MaxFramesPerPixel, FirstFrame = 0 };

    public WaveformViewport ScrollTo(double firstFrame) => this with { FirstFrame = Math.Clamp(firstFrame, 0, MaxFirstFrame) };

    public WaveformViewport ScrollBy(double pixels) => ScrollTo(FirstFrame + pixels * FramesPerPixel);

    /// <summary>Pages the view so <paramref name="frame"/> is visible, putting it near the left edge if it was off-screen.</summary>
    public WaveformViewport EnsureVisible(double frame) =>
        frame >= FirstFrame && frame <= FirstFrame + VisibleFrames ? this : ScrollTo(frame - VisibleFrames * 0.05);
}
