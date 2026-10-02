using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Keyboard;

namespace QuickParrot.Overlay.Tests;

public sealed class OverlayRendererTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.37f)]
    [InlineData(2f)]
    public void HeaderTextFitsItsSlots_IncludingTheActiveSaveCheckmark(float scale)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.LineLimit);
        foreach (var folder in new[] { "But Explain", "" })
        foreach (var kind in new[] { OverlayLayoutKind.Grid, OverlayLayoutKind.Wheel })
        foreach (var small in new[] { SmallFolderLayout.List, SmallFolderLayout.Ring })
        foreach (var key in new[] { ScanKey.DefaultSaveNavigationKey, new ScanKey(0x37, true), new ScanKey(0x3A, false), new ScanKey(0xFF, true) })
        {
            var state = (kind == OverlayLayoutKind.Grid ? ViewStates.Grid(18, folder) : ViewStates.Wheel(9, folder)) with
            {
                ShowSaveNavigationHint = true,
                SaveNavigationConfirmed = true,
                SaveNavigationKey = key,
                SmallFolderLayout = small,
                ZoomedColumn = kind == OverlayLayoutKind.Grid ? 1 : null,
            };
            var layout = OverlayLayoutGeometry.Compute(state, scale);
            var save = layout.SaveNavigationHint!.Label;

            AssertFits(layout.Title.Text, "Segoe UI Semibold", layout.Title.FontPx, layout.Title.Bounds.Width);
            AssertFits(save.Text + " ✓", "Segoe UI", save.FontPx, save.Bounds.Width);
            if (layout.Hint is { } hint)
            {
                using var font = new Font("Segoe UI", MathF.Round(hint.FontPx, 1), FontStyle.Regular, GraphicsUnit.Pixel);
                var actionWidth = graphics.MeasureString(hint.Action, font, PointF.Empty, format).Width;
                Assert.True(hint.FontPx * 1.8f + actionWidth <= hint.Bounds.Width);
            }
        }

        void AssertFits(string text, string family, float px, float width)
        {
            using var font = new Font(family, MathF.Round(px, 1), FontStyle.Regular, GraphicsUnit.Pixel);
            Assert.True(graphics.MeasureString(text, font, PointF.Empty, format).Width <= width, $"{text} would be truncated");
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.37f)]
    [InlineData(2f)]
    public void ConfigurableSaveKeysFitTheHeader_WithConfirmation(float scale)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.LineLimit);
        foreach (var extended in new[] { false, true })
        for (var code = 1; code <= 0xFF; code++)
        {
            var key = new ScanKey(code, extended);
            if (!key.IsValidSaveNavigationKey)
                continue;
            var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(18, "Quotes") with
            {
                ShowSaveNavigationHint = true,
                SaveNavigationConfirmed = true,
                SaveNavigationKey = key,
            }, scale);
            var label = layout.SaveNavigationHint!.Label;
            using var font = new Font("Segoe UI", MathF.Round(label.FontPx, 1), FontStyle.Regular, GraphicsUnit.Pixel);
            Assert.True(graphics.MeasureString(label.Text + " ✓", font, PointF.Empty, format).Width <= label.Bounds.Width,
                $"{label.Text} would be truncated");
        }
    }

    // UpdateLayeredWindow blends premultiplied pixels; a color channel above alpha would glow or fringe.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_IsValidPremultipliedAlpha_WithTransparentMargins(bool assigningFavorite)
    {
        var state = ViewStates.Grid(12, "Folder", zoomedColumn: 1);
        if (assigningFavorite)
            state = state with { Favorites = ViewStates.Favorites(target: 2) };

        var layout = OverlayLayoutGeometry.Compute(state, 0.5f);
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
