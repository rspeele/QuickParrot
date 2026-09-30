using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QuickParrot.Overlay.Tests;

public sealed class OverlayRendererTests
{
    // UpdateLayeredWindow blends premultiplied pixels; a color channel above alpha would glow or fringe.
    [Fact]
    public void Output_IsValidPremultipliedAlpha_WithTransparentMargins()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(12, "Folder", zoomedColumn: 1), 0.5f);
        using var bitmap = new Bitmap(layout.CanvasSize.Width, layout.CanvasSize.Height, PixelFormat.Format32bppPArgb);
        using (var renderer = new OverlayRenderer())
        using (var graphics = Graphics.FromImage(bitmap))
            renderer.Draw(graphics, layout);

        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var pixels = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        bitmap.UnlockBits(data);

        var partial = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            Assert.True(pixels[i] <= alpha && pixels[i + 1] <= alpha && pixels[i + 2] <= alpha);
            if (alpha is > 0 and < 255)
                partial++;
        }

        Assert.Equal(0, pixels[3]); // the corner is outside the rounded panel
        Assert.True(partial > pixels.Length / 8, "the panels should be translucent");
    }
}
