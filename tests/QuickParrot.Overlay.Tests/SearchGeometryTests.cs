using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

public sealed class SearchGeometryTests
{
    [Theory]
    [InlineData(SmallFolderLayout.List)]
    [InlineData(SmallFolderLayout.Ring)]
    public void SearchAlwaysShowsQueryAboveANumberedList(SmallFolderLayout preference)
    {
        var state = ViewStates.Wheel(9, "") with { SearchQuery = "number one", SmallFolderLayout = preference };
        var layout = OverlayLayoutGeometry.Compute(state, 1);
        Assert.Equal("Search library", layout.Title.Text);
        Assert.Equal("number one▏", layout.Subtitle!.Text);
        Assert.Contains("Enter plays 1", layout.Note!.Text);
        Assert.Equal(9, layout.Items.Count);
        Assert.True(layout.Items[0].Bounds.Top > layout.Panels[0].Bounds.Bottom);
        Assert.True(layout.Items[^1].Bounds.Bottom < layout.CanvasSize.Height);
        Assert.True(layout.Items.Zip(layout.Items.Skip(1)).All(pair => pair.First.Bounds.Bottom < pair.Second.Bounds.Top));
    }

    [Fact]
    public void EmptySearchHasPromptAndNoMatchesHasCancelHint()
    {
        var state = ViewStates.Wheel(0, "") with { SearchQuery = "" };
        var layout = OverlayLayoutGeometry.Compute(state, 1);
        Assert.Equal("Type a clip name…", layout.Subtitle!.Text);
        Assert.Equal("No matches · Esc cancels", layout.Note!.Text);
    }

    [Theory]
    [InlineData(800, 600, 192)]
    [InlineData(1920, 1080, 96)]
    [InlineData(3840, 2160, 192)]
    public void SearchFitsMonitor(int width, int height, int dpi)
    {
        var state = ViewStates.Wheel(9, "") with { SearchQuery = "query" };
        var layout = OverlayLayoutGeometry.ComputeForMonitor(state, new Size(width, height), dpi);
        Assert.True(layout.CanvasSize.Width < width);
        Assert.True(layout.CanvasSize.Height < height);
    }
}
