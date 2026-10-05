using System.Collections.Immutable;
using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

public sealed class FragmentGeometryTests
{
    [Fact]
    public void QueuedNamesKeepDots()
    {
        var state = ViewStates.Wheel(0) with
        {
            SearchQuery = "", IsFragmentSearch = true, FragmentNames = ["well.done"],
        };
        Assert.Equal("1. well.done", OverlayLayoutGeometry.Compute(state, 1).PhraseLabels[0].Text);
    }

    [Theory]
    [InlineData(800, 600, 192)]
    [InlineData(1920, 1080, 96)]
    [InlineData(3840, 2160, 192)]
    public void PhraseAndSearchFitWithoutOverlapping(int width, int height, int dpi)
    {
        var state = ViewStates.Wheel(9) with
        {
            SearchQuery = "next",
            IsFragmentSearch = true,
            FragmentSpeaker = "GLaDOS",
            FragmentNames = Enumerable.Range(1, 9).Select(i => $"word{i}").ToImmutableArray(),
            FragmentHoldProgress = 0.5,
        };
        var layout = OverlayLayoutGeometry.ComputeForMonitor(state, new Size(width, height), dpi);
        Assert.Contains("GLaDOS", layout.Title.Text);
        Assert.Equal("Speaker", layout.ColumnLabels[1].Text);
        Assert.Contains("Hold Enter 0.5s", layout.Note!.Text);
        Assert.Equal(10, layout.PhraseLabels.Count);
        Assert.All(layout.PhraseLabels.Take(9), label => Assert.True(label.Bounds.Bottom < layout.Subtitle!.Bounds.Top));
        Assert.All(layout.PhraseLabels, label => Assert.True(layout.Panels[0].Bounds.Contains(label.Bounds)));
        Assert.True(layout.Items[0].Bounds.Top > layout.Panels[0].Bounds.Bottom);
        Assert.True(layout.CanvasSize.Width < width);
        Assert.True(layout.CanvasSize.Height < height);
        Assert.True(layout.Panels[0].Bounds.Contains(layout.HoldProgressBounds));
    }

    [Fact]
    public void LongPhraseShowsRecentFragmentsInOrderWithEarlierCount()
    {
        var state = ViewStates.Wheel(0) with
        {
            SearchQuery = "",
            IsFragmentSearch = true,
            FragmentNames = Enumerable.Range(1, 20).Select(i => $"word{i}").ToImmutableArray(),
        };
        var layout = OverlayLayoutGeometry.Compute(state, 1);
        Assert.Contains("11 earlier", layout.Title.Text);
        Assert.Equal("12. word12", layout.PhraseLabels[0].Text);
        Assert.Equal("20. word20", layout.PhraseLabels[8].Text);
        Assert.True(layout.HoldProgressBounds.IsEmpty);
    }
}
