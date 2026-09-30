using System.Drawing;

namespace QuickParrot.Overlay.Tests;

public sealed class OverlayScaleTests
{
    private static readonly Size SmallCanvas = new(500, 300);

    [Theory]
    [InlineData(1920, 1080, 96, 1f)]
    [InlineData(2560, 1440, 96, 1.3333f)]
    [InlineData(3840, 2160, 96, 2f)]
    [InlineData(3840, 2160, 144, 2f)]
    [InlineData(1920, 1080, 144, 1.275f)] // small high-DPI laptop screen gets a bit bigger
    public void Scale_TracksMonitorHeight(int width, int height, int dpi, float expected)
    {
        Assert.Equal(expected, OverlayLayoutGeometry.ChooseScale(new Size(width, height), dpi, SmallCanvas), 0.001f);
    }

    [Fact]
    public void Scale_NeverOverflowsTheMonitor()
    {
        var scale = OverlayLayoutGeometry.ChooseScale(new Size(1024, 1080), 96, new Size(1600, 500));

        Assert.True(1600 * scale <= 1024);
    }

    [Fact]
    public void ComputeForMonitor_At4K_IsDoubleThe1080pSize()
    {
        var state = ViewStates.Wheel(9, "Movies");
        var hd = OverlayLayoutGeometry.ComputeForMonitor(state, new Size(1920, 1080), 96);
        var uhd = OverlayLayoutGeometry.ComputeForMonitor(state, new Size(3840, 2160), 144);

        Assert.Equal(1f, hd.Scale);
        Assert.Equal(2f, uhd.Scale);
    }

    [Fact]
    public void ComputeForMonitor_FullGridFitsA4By3Monitor()
    {
        var layout = OverlayLayoutGeometry.ComputeForMonitor(ViewStates.Grid(81), new Size(1280, 1024), 96);

        Assert.True(layout.CanvasSize.Width <= 1280);
        Assert.True(layout.CanvasSize.Height <= 1024);
    }

    [Fact]
    public void CenterOn_UsesTheMonitorsOwnOrigin()
    {
        var monitor = new Rectangle(1920, -200, 2560, 1440);

        Assert.Equal(new Point(1920 + 1030, -200 + 470), OverlayLayoutGeometry.CenterOn(monitor, new Size(500, 500)));
    }
}
