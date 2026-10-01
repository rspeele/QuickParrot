using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QuickParrot.Overlay.Tests;

public sealed class ToastGeometryTests
{
    private static readonly RectangleF Canvas1080 = new(0, 0, 1920, 1080);

    [Fact]
    public void Parts_SitInsideThePanel_InsideTheCanvas()
    {
        var toast = ToastGeometry.Compute("Grabbed last 30 s", 1);
        var canvas = new RectangleF(PointF.Empty, toast.CanvasSize);

        Assert.True(canvas.Contains(toast.Panel.Bounds));
        Assert.True(toast.Panel.Bounds.Contains(toast.IconBounds));
        Assert.True(toast.Panel.Bounds.Contains(toast.Text.Bounds));
        Assert.True(toast.IconBounds.Right < toast.Text.Bounds.Left);
        Assert.Equal(toast.Panel.Bounds.Height / 2, toast.Panel.CornerRadius);
    }

    [Fact]
    public void Panel_IsCenteredInTheCanvas()
    {
        var toast = ToastGeometry.Compute("Grabbed last 30 s", 1.5f);
        var panel = toast.Panel.Bounds;

        Assert.Equal(toast.CanvasSize.Width - panel.Right, panel.Left, 0.01f);
        Assert.Equal(toast.CanvasSize.Height - panel.Bottom, panel.Top, 0.01f);
    }

    [Fact]
    public void Width_GrowsWithText_WithinLimits()
    {
        var shortToast = ToastGeometry.Compute("Hi", 1);
        var medium = ToastGeometry.Compute("Nothing was playing in the last 30 s.", 1);
        var huge = ToastGeometry.Compute(new string('x', 500), 1);

        Assert.Equal(ToastGeometry.MinTextWidth, shortToast.Text.Bounds.Width, 0.01f);
        Assert.True(medium.CanvasSize.Width > shortToast.CanvasSize.Width);
        Assert.Equal(ToastGeometry.MaxTextWidth, huge.Text.Bounds.Width, 0.01f);
    }

    [Fact]
    public void Everything_ScalesTogether()
    {
        var one = ToastGeometry.Compute("Grabbed last 30 s", 1);
        var two = ToastGeometry.Compute("Grabbed last 30 s", 2);

        Assert.Equal(one.Panel.Bounds.Width * 2, two.Panel.Bounds.Width, 0.01f);
        Assert.Equal(one.Text.FontPx * 2, two.Text.FontPx, 0.01f);
        Assert.Equal(2, two.Scale);
    }

    [Fact]
    public void ComputeForMonitor_MatchesTheOverlayScale()
    {
        Assert.Equal(1, ToastGeometry.ComputeForMonitor("Grabbed", new Size(1920, 1080), 96).Scale, 0.01f);
        Assert.Equal(2, ToastGeometry.ComputeForMonitor("Grabbed", new Size(3840, 2160), 144).Scale, 0.01f);
    }

    [Fact]
    public void ComputeForMonitor_NeverOverflowsATinyMonitor()
    {
        var toast = ToastGeometry.ComputeForMonitor(new string('x', 500), new Size(400, 300), 96);

        Assert.True(toast.CanvasSize.Width <= 400);
    }

    [Fact]
    public void PositionOn_IsTopCenterOfTheMonitor()
    {
        var monitor = new Rectangle(-1920, 100, 1920, 1080);
        var canvas = new Size(400, 60);

        var at = ToastGeometry.PositionOn(monitor, canvas);

        Assert.Equal(-1920 + 760, at.X);
        Assert.Equal(100 + (int)(1080 * ToastGeometry.TopFraction), at.Y);
        Assert.True(Canvas1080.Contains(new RectangleF(at.X + 1920, at.Y - 100, canvas.Width, canvas.Height)));
    }

    // UpdateLayeredWindow blends premultiplied pixels; a color channel above alpha would glow or fringe.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rendered_IsValidPremultipliedAlpha_WithTransparentCorners(bool isError)
    {
        var toast = ToastGeometry.Compute("Couldn't save the grab: disk full", 1, isError);
        using var bitmap = new Bitmap(toast.CanvasSize.Width, toast.CanvasSize.Height, PixelFormat.Format32bppPArgb);
        using (var renderer = new OverlayRenderer())
        using (var graphics = Graphics.FromImage(bitmap))
            renderer.DrawToast(graphics, toast);

        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var pixels = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        bitmap.UnlockBits(data);

        for (var i = 0; i < pixels.Length; i += 4)
            Assert.True(pixels[i] <= pixels[i + 3] && pixels[i + 1] <= pixels[i + 3] && pixels[i + 2] <= pixels[i + 3]);

        Assert.Equal(0, pixels[3]);
        var center = (data.Height / 2 * data.Stride) + (data.Width / 2 * 4);
        Assert.True(pixels[center + 3] > 200, "the panel should be nearly opaque");
    }
}
