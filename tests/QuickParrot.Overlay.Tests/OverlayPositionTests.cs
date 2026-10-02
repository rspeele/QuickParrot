using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

public sealed class OverlayPositionTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1080, 96)]
    [InlineData(-3840, -200, 3840, 2160, 144)]
    [InlineData(1920, 100, 1280, 720, 144)]
    [InlineData(-320, -240, 320, 240, 192)]
    public void FirstRowStaysAtTheSameScreenHeight_AcrossListAndGridSizes(
        int x, int y, int width, int height, int dpi)
    {
        var monitor = new Rectangle(x, y, width, height);
        foreach (var count in new[] { 1, 2, 9, 10, 18, 27, 81, 100 })
        foreach (var saveState in new[] { 0, 1, 2 })
        foreach (var favorites in new[] { false, true })
        {
            var state = (count > 9 ? ViewStates.Grid(count, "Quotes") : ViewStates.Wheel(count, "Quotes")) with
            {
                ShowSaveNavigationHint = saveState > 0,
                SaveNavigationConfirmed = saveState == 2,
                Favorites = favorites ? ViewStates.Favorites(3) : null,
            };
            var layout = OverlayLayoutGeometry.ComputeForMonitor(state, monitor.Size, dpi);
            var position = OverlayLayoutGeometry.PositionOn(monitor, layout);
            var firstCenter = ScreenCenterY(layout.Items[0].Bounds, position, layout);

            Assert.InRange(firstCenter, y + height * (444f / 1080) - 0.51f, y + height * (444f / 1080) + 0.51f);
            Assert.True(monitor.Contains(new Rectangle(position, layout.CanvasSize)));
            Assert.Equal(x + (width - layout.CanvasSize.Width) / 2, position.X);
        }
    }

    [Theory]
    [InlineData(1920, 1080, 96)]
    [InlineData(1280, 720, 144)]
    [InlineData(320, 240, 192)]
    public void ShortAndLongListsKeepRowsOneAndTwoInPlace_WithoutBlankWindowRows(int width, int height, int dpi)
    {
        var monitor = new Rectangle(100, -100, width, height);
        var shortList = OverlayLayoutGeometry.ComputeForMonitor(ViewStates.Wheel(2), monitor.Size, dpi);
        var longList = OverlayLayoutGeometry.ComputeForMonitor(ViewStates.Wheel(9), monitor.Size, dpi);
        var shortPosition = OverlayLayoutGeometry.PositionOn(monitor, shortList);
        var longPosition = OverlayLayoutGeometry.PositionOn(monitor, longList);

        Assert.Equal(longList.Scale, shortList.Scale);
        Assert.True(shortList.CanvasSize.Height < longList.CanvasSize.Height);
        for (var row = 0; row < 2; row++)
            Assert.Equal(ScreenCenterY(longList.Items[row].Bounds, longPosition, longList),
                ScreenCenterY(shortList.Items[row].Bounds, shortPosition, shortList));
    }

    [Fact]
    public void WideGridCanShrinkToFit_WithoutMovingTheFirstRow()
    {
        var monitor = new Rectangle(0, 0, 1280, 1024);
        var list = OverlayLayoutGeometry.ComputeForMonitor(ViewStates.Wheel(9), monitor.Size, 96);
        var grid = OverlayLayoutGeometry.ComputeForMonitor(ViewStates.Grid(81), monitor.Size, 96);

        Assert.True(grid.Scale < list.Scale);
        var listCenter = ScreenCenterY(list.Items[0].Bounds, OverlayLayoutGeometry.PositionOn(monitor, list), list);
        var gridCenter = ScreenCenterY(grid.Items[0].Bounds, OverlayLayoutGeometry.PositionOn(monitor, grid), grid);
        Assert.Equal(listCenter, gridCenter, 1f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyListKeepsTheHeaderPosition_AndFitsWithFavorites(bool favorites)
    {
        var monitor = new Rectangle(-1280, 100, 1280, 720);
        var emptyState = ViewStates.Wheel(0, "Empty") with { Favorites = favorites ? ViewStates.Favorites(2) : null };
        var fullState = ViewStates.Wheel(9, "Full") with { Favorites = emptyState.Favorites };
        var empty = OverlayLayoutGeometry.ComputeForMonitor(emptyState, monitor.Size, 144);
        var full = OverlayLayoutGeometry.ComputeForMonitor(fullState, monitor.Size, 144);
        var emptyPosition = OverlayLayoutGeometry.PositionOn(monitor, empty);
        var fullPosition = OverlayLayoutGeometry.PositionOn(monitor, full);

        Assert.Equal(full.Scale, empty.Scale);
        Assert.Equal(fullPosition.Y + full.FolderOrigin.Y + full.Title.Bounds.Top,
            emptyPosition.Y + empty.FolderOrigin.Y + empty.Title.Bounds.Top);
        Assert.True(monitor.Contains(new Rectangle(emptyPosition, empty.CanvasSize)));
        Assert.NotNull(empty.Subtitle);
    }

    [Fact]
    public void StandardGridKeepsItsPreviousCenteredPosition()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var layout = OverlayLayoutGeometry.ComputeForMonitor(ViewStates.Grid(18), monitor.Size, 96);

        Assert.Equal(OverlayLayoutGeometry.CenterOn(monitor, layout.CanvasSize), OverlayLayoutGeometry.PositionOn(monitor, layout));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    public void RingModeRemainsCentered(int count, bool favorites)
    {
        var monitor = new Rectangle(1920, -200, 1280, 1024);
        var state = ViewStates.Wheel(count) with
        {
            SmallFolderLayout = SmallFolderLayout.Ring,
            Favorites = favorites ? ViewStates.Favorites(1) : null,
        };
        var layout = OverlayLayoutGeometry.ComputeForMonitor(state, monitor.Size, 144);

        Assert.Null(layout.FirstRowCenterY);
        Assert.Equal(OverlayLayoutGeometry.CenterOn(monitor, layout.CanvasSize), OverlayLayoutGeometry.PositionOn(monitor, layout));
        Assert.Equal(OverlayLayoutGeometry.ChooseScale(monitor.Size, 144, OverlayLayoutGeometry.Compute(state, 1).CanvasSize), layout.Scale);
    }

    private static float ScreenCenterY(RectangleF bounds, Point position, OverlayLayout layout) =>
        position.Y + layout.FolderOrigin.Y + bounds.Top + bounds.Height / 2;
}
