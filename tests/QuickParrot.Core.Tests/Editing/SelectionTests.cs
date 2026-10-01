using QuickParrot.Core.Editing;
using static QuickParrot.Core.Tests.Editing.TestSignals;

namespace QuickParrot.Core.Tests.Editing;

public class ClipSelectionTests
{
    [Theory]
    [InlineData(100, 500, 100, 500)]
    [InlineData(500, 100, 100, 500)]
    [InlineData(-50, 2000, 0, 1000)]
    [InlineData(100, 120, 100, 150)] // grown away from the anchor
    [InlineData(100, 80, 50, 100)]   // ...in the drag direction
    [InlineData(990, 995, 950, 1000)] // no room past the end, so grown back over the anchor
    [InlineData(10, 5, 0, 50)]
    public void FromPoints_OrdersClampsAndEnforcesMinimumLength(int anchor, int other, int start, int end)
    {
        Assert.Equal(new ClipSelection(start, end), ClipSelection.FromPoints(anchor, other, 1000, 50));
    }

    [Fact]
    public void FromPoints_OnAudioShorterThanTheMinimum_SelectsItAll()
    {
        Assert.Equal(new ClipSelection(0, 30), ClipSelection.FromPoints(10, 12, 30, 50));
    }
}

public class SelectionGestureTests
{
    private static readonly WaveformViewport Viewport = new(10000, 1000); // 10 frames per pixel

    [Fact]
    public void DraggingEmptySpace_SelectsFromThePressPoint()
    {
        var gesture = SelectionGesture.Begin(Viewport, null, 100, 50);

        Assert.Null(gesture.Move(101)); // still within click distance
        Assert.Equal(new ClipSelection(1000, 3000), gesture.Move(300));
        Assert.Equal(new ClipSelection(500, 1000), gesture.Move(50));
        Assert.False(gesture.IsClick);
    }

    [Fact]
    public void PressWithoutDragging_IsAClick()
    {
        var gesture = SelectionGesture.Begin(Viewport, new ClipSelection(1000, 3000), 600, 50);

        gesture.Move(601);

        Assert.True(gesture.IsClick);
        Assert.Equal(6000, gesture.ClickFrame);
    }

    [Theory]
    [InlineData(103, SelectionDragTarget.StartHandle)]
    [InlineData(296, SelectionDragTarget.EndHandle)]
    [InlineData(200, SelectionDragTarget.NewSelection)]
    [InlineData(80, SelectionDragTarget.NewSelection)]
    public void HitTest_FindsTheNearestHandleWithinTolerance(double x, SelectionDragTarget expected)
    {
        Assert.Equal(expected, SelectionGesture.HitTest(Viewport, new ClipSelection(1000, 3000), x));
    }

    [Fact]
    public void DraggingAHandle_MovesThatEdge_AndCanCrossTheOther()
    {
        var selection = new ClipSelection(1000, 3000);
        var gesture = SelectionGesture.Begin(Viewport, selection, 100, 50);

        Assert.Equal(new ClipSelection(1500, 3000), gesture.Move(150));
        Assert.Equal(new ClipSelection(3000, 4000), gesture.Move(400));
    }

    [Fact]
    public void Anchor_IsTheOppositeHandle_OrThePressFrameForANewSelection()
    {
        var selection = new ClipSelection(1000, 3000);

        Assert.Equal(3000, SelectionGesture.Begin(Viewport, selection, 103, 50).Anchor); // start handle: anchored at the end
        Assert.Equal(1000, SelectionGesture.Begin(Viewport, selection, 296, 50).Anchor); // end handle: anchored at the start
        Assert.Equal(2000, SelectionGesture.Begin(Viewport, null, 200, 50).Anchor);      // new selection: anchored at the press point
    }

    [Fact]
    public void Dragging_EnforcesTheMinimumLength()
    {
        var gesture = SelectionGesture.Begin(Viewport, new ClipSelection(1000, 3000), 300, 500);

        Assert.Equal(new ClipSelection(1000, 1500), gesture.Move(101));
    }
}

public class QuietPointSnapperTests
{
    [Fact]
    public void SnapsIntoANearbyGap()
    {
        // Loud noise with a 2 ms gap 3 ms after the requested edge.
        var samples = PinkNoise(-6, 0.1, channels: 1);
        var gapStart = 2400 + 144;
        Array.Clear(samples, gapStart, 96);
        var audio = Audio(samples, channels: 1);

        var snapped = QuietPointSnapper.Snap(audio, 2400, audio.FramesFor(TimeSpan.FromMilliseconds(5)));

        Assert.InRange(snapped, gapStart, gapStart + 96);
    }

    [Fact]
    public void OnASine_LandsNearAZeroCrossing()
    {
        var audio = Audio(Sine(100, -6, 0.1, channels: 1), channels: 1);

        var snapped = QuietPointSnapper.Snap(audio, 1000, 240);

        Assert.True(Math.Abs(audio.Samples.Span[snapped]) < 0.01, $"sample {audio.Samples.Span[snapped]}");
    }

    [Fact]
    public void InSilence_StaysPut_AndRespectsTheEnds()
    {
        var audio = Audio(Silence(0.1));

        Assert.Equal(2000, QuietPointSnapper.Snap(audio, 2000, 240));
        Assert.Equal(audio.FrameCount, QuietPointSnapper.Snap(audio, audio.FrameCount + 50, 240));
        Assert.Equal(0, QuietPointSnapper.Snap(audio, 0, 240));
    }

    [Fact]
    public void SnappingASelection_KeepsItAtLeastTheMinimumLength()
    {
        var audio = Audio(Sine(100, -6, 0.1, channels: 1), channels: 1);
        var selection = new ClipSelection(1000, 1100);

        var snapped = QuietPointSnapper.Snap(audio, selection, TimeSpan.FromMilliseconds(5), 90);

        Assert.True(snapped.Length >= 90);
    }
}

public class ClipFadesTests
{
    [Fact]
    public void FadesBothEnds_AndLeavesTheMiddle()
    {
        var samples = Enumerable.Repeat(1f, 200).ToArray(); // 100 stereo frames

        ClipFades.Apply(samples, 2, 10);

        Assert.Equal(0f, samples[0]);
        Assert.Equal(0f, samples[^1]);
        Assert.True(samples[2] > 0 && samples[2] < samples[18]);
        Assert.All(samples[20..180], s => Assert.Equal(1f, s));
    }

    [Fact]
    public void TinyClip_FadesOverHalfItsLength()
    {
        var samples = Enumerable.Repeat(1f, 6).ToArray();

        ClipFades.Apply(samples, 1, 100);

        Assert.Equal([0f, 0.25f, 0.75f, 0.75f, 0.25f, 0f], samples.Select(s => MathF.Round(s, 3)).ToArray());
    }
}

public class SilenceTrimmerTests
{
    [Fact]
    public void TrimsLeadingAndTrailingSilence_KeepingPadding()
    {
        var audio = Audio(Concat(Silence(1), Sine(440, -12, 1), PinkNoise(-80, 1)));

        var selection = SilenceTrimmer.Suggest(audio);

        Assert.InRange(selection.Start, 48000 - 4800 - 480, 48000 - 4800);
        Assert.InRange(selection.End, 96000 + 4800, 96000 + 4800 + 480);
    }

    [Fact]
    public void AllSilence_SelectsEverything()
    {
        var audio = Audio(Silence(0.5));

        Assert.Equal(ClipSelection.All(audio.FrameCount), SilenceTrimmer.Suggest(audio));
    }
}
