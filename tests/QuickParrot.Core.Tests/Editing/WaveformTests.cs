using QuickParrot.Core.Editing;
using static QuickParrot.Core.Tests.Editing.TestSignals;

namespace QuickParrot.Core.Tests.Editing;

public class WaveformPeaksTests
{
    private static (float Min, float Max) BruteForce(EditableAudio audio, int start, int end)
    {
        var span = audio.Frames(start, end);
        return (span.ToArray().Min(), span.ToArray().Max());
    }

    [Theory]
    [InlineData(32.0)]
    [InlineData(128.0)]
    [InlineData(512.0)]
    [InlineData(2048.0)]
    public void BucketAlignedColumns_MatchBruteForce(double framesPerColumn)
    {
        var audio = Audio(PinkNoise(-12, 0.5));
        var peaks = new WaveformPeaks(audio);
        var columns = (int)(audio.FrameCount / framesPerColumn);
        var mins = new float[columns];
        var maxs = new float[columns];

        peaks.GetColumns(0, framesPerColumn, mins, maxs);

        for (var i = 0; i < columns; i++)
        {
            var (min, max) = BruteForce(audio, (int)(i * framesPerColumn), (int)((i + 1) * framesPerColumn));
            Assert.Equal(min, mins[i]);
            Assert.Equal(max, maxs[i]);
        }
    }

    [Theory]
    [InlineData(0.3, 7.0)]
    [InlineData(1234.5, 100.7)]
    [InlineData(99.9, 3333.3)]
    public void ArbitraryColumns_CoverAtLeastTheirFramesAndAtMostOneBucketMore(double first, double framesPerColumn)
    {
        var audio = Audio(PinkNoise(-12, 0.5, channels: 1), channels: 1);
        var peaks = new WaveformPeaks(audio);
        var columns = Math.Min(50, (int)((audio.FrameCount - first) / framesPerColumn));
        var mins = new float[columns];
        var maxs = new float[columns];

        peaks.GetColumns(first, framesPerColumn, mins, maxs);

        for (var i = 0; i < mins.Length; i++)
        {
            var start = (int)Math.Floor(first + i * framesPerColumn);
            var end = (int)Math.Ceiling(first + (i + 1) * framesPerColumn);
            var (innerMin, innerMax) = BruteForce(audio, start, end);
            var slack = (int)framesPerColumn + WaveformPeaks.BaseBucketFrames;
            var (outerMin, outerMax) = BruteForce(audio, Math.Max(0, start - slack), Math.Min(audio.FrameCount, end + slack));
            Assert.InRange(mins[i], outerMin, innerMin);
            Assert.InRange(maxs[i], innerMax, outerMax);
        }
    }

    [Fact]
    public void ColumnsPastTheEnd_AreNaN_AndSubFrameColumnsShowTheirFrame()
    {
        var audio = Audio([0.5f, -0.25f, 0.75f], channels: 1);
        var peaks = new WaveformPeaks(audio);
        var mins = new float[8];
        var maxs = new float[8];

        peaks.GetColumns(0, 0.5, mins, maxs);

        Assert.Equal([0.5f, 0.5f, -0.25f, -0.25f, 0.75f, 0.75f], maxs[..6]);
        Assert.True(float.IsNaN(maxs[6]) && float.IsNaN(mins[7]));
    }

    [Fact]
    public void MergesChannels_AndBuildsLevelsUpToOneBucket()
    {
        var samples = new float[48000 * 2];
        samples[1000] = 0.9f;   // left
        samples[50001] = -0.8f; // right
        var peaks = new WaveformPeaks(Audio(samples));
        var mins = new float[1];
        var maxs = new float[1];

        peaks.GetColumns(0, 48000, mins, maxs);

        Assert.Equal((-0.8f, 0.9f), (mins[0], maxs[0]));
        Assert.True(WaveformPeaks.BucketFrames(peaks.LevelCount - 1) >= 48000);
    }
}

public class WaveformViewportTests
{
    [Fact]
    public void StartsFullyZoomedOut()
    {
        var viewport = new WaveformViewport(48000, 480);

        Assert.Equal(100, viewport.FramesPerPixel);
        Assert.Equal(0, viewport.FirstFrame);
        Assert.False(viewport.IsZoomedIn);
        Assert.Equal(24000, viewport.FrameAt(240));
        Assert.Equal(240, viewport.XAt(24000));
    }

    [Fact]
    public void ZoomAround_KeepsTheFrameUnderTheCursorInPlace()
    {
        var viewport = new WaveformViewport(48000, 480).ZoomAround(120, 4);

        Assert.Equal(25, viewport.FramesPerPixel);
        Assert.Equal(12000, viewport.FrameAt(120), 6);
        Assert.True(viewport.IsZoomedIn);
    }

    [Fact]
    public void Transitions_LeaveTheOriginalUnchanged()
    {
        var viewport = new WaveformViewport(48000, 480);

        viewport.ZoomAround(120, 4).ScrollBy(10);

        Assert.Equal(new WaveformViewport(48000, 480), viewport);
    }

    [Fact]
    public void Zoom_IsClampedBothWays_AndScrollStaysInsideTheAudio()
    {
        var viewport = new WaveformViewport(48000, 480).ZoomAround(480, 1e9);
        Assert.Equal(WaveformViewport.MinFramesPerPixel, viewport.FramesPerPixel);
        Assert.Equal(viewport.MaxFirstFrame, viewport.FirstFrame, 6);

        viewport = viewport.ScrollTo(1e9);
        Assert.Equal(48000 - viewport.VisibleFrames, viewport.FirstFrame, 6);

        viewport = viewport.ZoomAround(0, 1e-9);
        Assert.Equal(100, viewport.FramesPerPixel);
        Assert.Equal(0, viewport.FirstFrame);
    }

    [Fact]
    public void Resizing_StaysFittedWhenZoomedOut_AndKeepsZoomWhenZoomedIn()
    {
        var viewport = new WaveformViewport(48000, 480).WithWidth(960);
        Assert.Equal(50, viewport.FramesPerPixel);

        viewport = viewport.ZoomAround(0, 5).WithWidth(480);
        Assert.Equal(10, viewport.FramesPerPixel);
    }

    [Fact]
    public void ShowAll_FitsTheWholeCaptureAgain()
    {
        var viewport = new WaveformViewport(48000, 480).ZoomAround(240, 8).ShowAll();

        Assert.Equal(100, viewport.FramesPerPixel);
        Assert.Equal(0, viewport.FirstFrame);
        Assert.Equal(50, viewport.WithWidth(960).FramesPerPixel);
    }

    [Fact]
    public void EnsureVisible_PagesToAnOffScreenFrame()
    {
        var viewport = new WaveformViewport(48000, 100).ZoomAround(0, 10); // 48 frames per pixel, 4800 visible

        Assert.Same(viewport, viewport.EnsureVisible(1000));
        var paged = viewport.EnsureVisible(10000);
        Assert.InRange(10000.0, paged.FirstFrame, paged.FirstFrame + paged.VisibleFrames);
    }

    [Fact]
    public void ScrollBy_MovesByPixels()
    {
        var viewport = new WaveformViewport(48000, 100).ZoomAround(0, 10).ScrollBy(10);

        Assert.Equal(480, viewport.FirstFrame);
    }
}

public class TimeFormattingTests
{
    [Theory]
    [InlineData(0, "0:00.000")]
    [InlineData(12.3456, "0:12.346")]
    [InlineData(62.5, "1:02.500")]
    [InlineData(-1, "0:00.000")]
    public void Position_IsMinutesSecondsMilliseconds(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormatting.Position(seconds));
    }

    [Theory]
    [InlineData(5, 1, "0:05")]
    [InlineData(65, 5, "1:05")]
    [InlineData(5.5, 0.5, "0:05.5")]
    [InlineData(5.25, 0.05, "0:05.25")]
    [InlineData(5.125, 0.005, "0:05.125")]
    public void Tick_ShowsJustEnoughDecimals(double seconds, double step, string expected)
    {
        Assert.Equal(expected, TimeFormatting.Tick(seconds, step));
    }

    [Theory]
    [InlineData(0.01, 70, 1)]      // 100 px per second
    [InlineData(0.001, 70, 0.1)]
    [InlineData(1.0, 70, 120)]
    [InlineData(100.0, 70, 300)]
    public void RulerStep_IsTheSmallestNiceStepThatFits(double secondsPerPixel, double minSpacing, double expected)
    {
        Assert.Equal(expected, TimeFormatting.RulerStep(secondsPerPixel, minSpacing));
    }
}
