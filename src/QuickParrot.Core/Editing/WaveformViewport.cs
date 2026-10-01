namespace QuickParrot.Core.Editing;

/// <summary>Maps frames to horizontal positions for a waveform of a given width, with zoom and scroll.</summary>
public sealed class WaveformViewport
{
    /// <summary>The closest zoom: a few pixels per frame, enough to see individual samples.</summary>
    public const double MinFramesPerPixel = 1 / 8.0;

    private bool _fitsAll = true;

    public WaveformViewport(int totalFrames, double width = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalFrames, 0);
        TotalFrames = totalFrames;
        Width = Math.Max(1, width);
        FramesPerPixel = MaxFramesPerPixel;
    }

    public int TotalFrames { get; }

    public double Width { get; private set; }

    public double FirstFrame { get; private set; }

    public double FramesPerPixel { get; private set; }

    public double MaxFramesPerPixel => Math.Max(MinFramesPerPixel, TotalFrames / Width);

    public double VisibleFrames => Width * FramesPerPixel;

    public double MaxFirstFrame => Math.Max(0, TotalFrames - VisibleFrames);

    public bool IsZoomedIn => FramesPerPixel < MaxFramesPerPixel;

    public double FrameAt(double x) => FirstFrame + x * FramesPerPixel;

    public double XAt(double frame) => (frame - FirstFrame) / FramesPerPixel;

    /// <summary>Resizes, keeping the zoom (or staying fully zoomed out) and the left edge where possible.</summary>
    public void SetWidth(double width)
    {
        Width = Math.Max(1, width);
        FramesPerPixel = _fitsAll ? MaxFramesPerPixel : Math.Clamp(FramesPerPixel, MinFramesPerPixel, MaxFramesPerPixel);
        ScrollTo(FirstFrame);
    }

    /// <summary>Zooms by <paramref name="factor"/> (&gt;1 zooms in) keeping the frame under <paramref name="x"/> in place.</summary>
    public void ZoomAround(double x, double factor)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor));

        var anchor = FrameAt(x);
        FramesPerPixel = Math.Clamp(FramesPerPixel / factor, MinFramesPerPixel, MaxFramesPerPixel);
        _fitsAll = FramesPerPixel >= MaxFramesPerPixel;
        ScrollTo(anchor - x * FramesPerPixel);
    }

    public void ShowAll()
    {
        _fitsAll = true;
        FramesPerPixel = MaxFramesPerPixel;
        FirstFrame = 0;
    }

    public void ScrollTo(double firstFrame) => FirstFrame = Math.Clamp(firstFrame, 0, MaxFirstFrame);

    public void ScrollBy(double pixels) => ScrollTo(FirstFrame + pixels * FramesPerPixel);

    /// <summary>Pages the view so <paramref name="frame"/> is visible, putting it near the left edge if it was off-screen.</summary>
    public bool EnsureVisible(double frame)
    {
        if (frame >= FirstFrame && frame <= FirstFrame + VisibleFrames)
            return false;

        var before = FirstFrame;
        ScrollTo(frame - VisibleFrames * 0.05);
        return FirstFrame != before;
    }
}
