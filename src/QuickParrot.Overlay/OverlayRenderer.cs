using System.Drawing.Drawing2D;
using System.Drawing.Text;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>
/// Draws an <see cref="OverlayLayout"/> with GDI+. Grayscale antialiasing only: ClearType needs an opaque
/// background, and this is drawn onto transparency. Expects a cleared, transparent PArgb target. Not thread-safe.
/// </summary>
public sealed class OverlayRenderer : IDisposable
{
    private static readonly Color Accent = Color.FromArgb(164, 207, 176);
    private static readonly Color FolderColor = Color.FromArgb(246, 196, 83);
    private static readonly Color TextColor = Color.FromArgb(244, 246, 250);
    private static readonly Color MutedText = Color.FromArgb(170, 178, 192);
    private static readonly Color PanelFill = Color.FromArgb(240, 14, 16, 22);
    private static readonly Color PanelBorder = Color.FromArgb(46, 255, 255, 255);
    private static readonly Color PillFill = Color.FromArgb(240, 16, 18, 24);
    private static readonly Color CellFill = Color.FromArgb(160, 40, 44, 54);
    private static readonly Color HighlightFill = Color.FromArgb(36, Accent);
    private static readonly Color ErrorColor = Color.FromArgb(255, 122, 89);
    private static readonly Color GlyphInk = Color.FromArgb(14, 16, 22);
    private const int DimmedAlpha = 105;
    private const string TextFamily = "Segoe UI Semibold";
    private const string NumberFamily = "Segoe UI";
    private const int MaxCachedFonts = 64; // a few scales' worth

    private readonly Dictionary<(string Family, float Px, FontStyle Style), Font> _fonts = [];
    private readonly StringFormat[] _formats =
        [MakeFormat(StringAlignment.Near), MakeFormat(StringAlignment.Center), MakeFormat(StringAlignment.Far)];

    public void Draw(Graphics g, OverlayLayout layout)
    {
        Prepare(g);
        if (layout.Favorites is { } favorites)
            DrawFavorites(g, favorites, layout.Scale);

        var untranslated = g.Save();
        g.TranslateTransform(layout.FolderOrigin.X, layout.FolderOrigin.Y);
        // The first panel lands on bare transparency, so copying instead of blending halves its cost.
        for (var i = 0; i < layout.Panels.Count; i++)
            DrawPanel(g, layout.Panels[i], layout.Scale, copyFill: i == 0);

        foreach (var header in layout.Headers)
        {
            var color = header.Dimmed ? Fade(MutedText) : Accent;
            DrawText(g, header.Number.ToString(), header.Bounds, Font(NumberFamily, header.FontPx, FontStyle.Bold), color, OverlayTextAlign.Center);
        }

        foreach (var item in layout.Items)
            DrawItem(g, item, layout);

        DrawLabel(g, layout.Title, TextColor);
        if (layout.SaveNavigationHint is { } saveHint)
        {
            var label = saveHint.Label;
            DrawLabel(g, saveHint.Active ? label with { Text = label.Text + " ✓" } : label,
                saveHint.Active ? Accent : Fade(MutedText));
        }
        DrawLabel(g, layout.Subtitle, MutedText);
        DrawHint(g, layout.Hint, layout.Scale);
        DrawLabel(g, layout.Note, MutedText);
        g.Restore(untranslated);
    }

    public void DrawToast(Graphics g, ToastLayout toast)
    {
        Prepare(g);
        DrawPanel(g, toast.Panel, toast.Scale, copyFill: true);
        DrawStatusGlyph(g, toast.IconBounds, toast.IsError, toast.Scale);
        DrawLabel(g, toast.Text, TextColor);
    }

    public void Dispose()
    {
        ClearFonts();
        foreach (var format in _formats)
            format.Dispose();
    }

    // Only between draws: fonts handed out during one must stay alive until it ends.
    private void Prepare(Graphics g)
    {
        if (_fonts.Count > MaxCachedFonts)
            ClearFonts();

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    private void ClearFonts()
    {
        foreach (var font in _fonts.Values)
            font.Dispose();

        _fonts.Clear();
    }

    private static void DrawPanel(Graphics g, OverlayPanel panel, float scale, bool copyFill)
    {
        var highlighted = panel.Style == OverlayPanelStyle.HighlightedColumn;
        using var path = RoundedRect(panel.Bounds, panel.CornerRadius);
        using var fill = new SolidBrush(highlighted ? HighlightFill : PanelFill);
        using var border = new Pen(highlighted ? Accent : PanelBorder, (highlighted ? 1.5f : 1f) * scale);
        g.CompositingMode = copyFill ? CompositingMode.SourceCopy : CompositingMode.SourceOver;
        g.FillPath(fill, path);
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawPath(border, path);
    }

    private void DrawItem(Graphics g, OverlayItem item, OverlayLayout layout)
    {
        var wheel = layout.Kind == OverlayLayoutKind.Wheel;
        var fillColor = wheel ? PillFill : CellFill;
        using (var path = RoundedRect(item.Bounds, item.CornerRadius))
        {
            using var fill = new SolidBrush(item.Dimmed ? Color.FromArgb(fillColor.A / 2, fillColor) : fillColor);
            g.FillPath(fill, path);
            if (wheel)
            {
                using var border = new Pen(item.IsFolder ? Color.FromArgb(90, FolderColor) : PanelBorder, layout.Scale);
                g.DrawPath(border, path);
            }
        }

        if (!item.NumberBounds.IsEmpty)
        {
            using var badge = RoundedRect(item.NumberBounds, wheel ? item.NumberBounds.Height / 2 : item.CornerRadius - 2);
            using var badgeFill = new SolidBrush(Color.FromArgb(48, Accent));
            g.FillPath(badgeFill, badge);
            DrawText(g, item.Number.ToString(), item.NumberBounds, Font(NumberFamily, item.NumberFontPx, FontStyle.Bold), Accent, OverlayTextAlign.Center);
        }

        if (!item.IconBounds.IsEmpty)
            DrawFolderGlyph(g, item.IconBounds, item.Dimmed ? Fade(FolderColor) : FolderColor);

        var nameColor = item.Dimmed ? Fade(TextColor) : TextColor;
        DrawText(g, item.Name, item.NameBounds, Font(TextFamily, item.NameFontPx, FontStyle.Regular), nameColor, OverlayTextAlign.Near);
    }

    private void DrawFavorites(Graphics g, FavoritesStrip strip, float scale)
    {
        DrawPanel(g, strip.Panel, scale, copyFill: true);
        DrawLabel(g, strip.Title, Accent);
        DrawLabel(g, strip.Instructions, MutedText);
        foreach (var slot in strip.Slots)
            DrawFavoriteSlot(g, slot, scale);
    }

    private void DrawFavoriteSlot(Graphics g, FavoriteSlotItem slot, float scale)
    {
        var faded = slot.State is FavoriteSlotState.Empty or FavoriteSlotState.Unavailable;
        using (var path = RoundedRect(slot.Bounds, slot.CornerRadius))
        {
            using var fill = new SolidBrush(faded && !slot.Target ? Color.FromArgb(CellFill.A / 2, CellFill) : CellFill);
            g.FillPath(fill, path);
            if (slot.Target)
            {
                using var highlight = new SolidBrush(HighlightFill);
                g.FillPath(highlight, path);
            }

            var borderColor = slot.Target ? Accent
                : slot.State == FavoriteSlotState.Missing ? Color.FromArgb(150, ErrorColor)
                : PanelBorder;
            using var border = new Pen(borderColor, (slot.Target ? 1.5f : 1f) * scale);
            g.DrawPath(border, path);
        }

        var keyColor = slot.Target || !faded ? Accent : Fade(MutedText);
        DrawText(g, slot.KeyLabel, slot.KeyBounds, Font(NumberFamily, slot.KeyFontPx, FontStyle.Bold), keyColor, OverlayTextAlign.Near);
        if (slot.Tag is { } tag)
        {
            var tagColor = slot.State == FavoriteSlotState.Missing ? ErrorColor : MutedText;
            DrawText(g, tag, slot.TagBounds, Font(NumberFamily, slot.TagFontPx, FontStyle.Regular), tagColor, OverlayTextAlign.Far);
        }

        var nameColor = slot.State switch
        {
            FavoriteSlotState.Assigned => TextColor,
            FavoriteSlotState.Missing => Fade(TextColor),
            _ => Fade(MutedText),
        };
        DrawText(g, slot.Name, slot.NameBounds, Font(TextFamily, slot.NameFontPx, FontStyle.Regular), nameColor, OverlayTextAlign.Near);
    }

    private static void DrawFolderGlyph(Graphics g, RectangleF r, Color color)
    {
        var radius = r.Height * 0.14f;
        using var brush = new SolidBrush(color);
        using var tab = RoundedRect(new RectangleF(r.X, r.Y, r.Width * 0.45f, r.Height * 0.4f), radius);
        using var body = RoundedRect(new RectangleF(r.X, r.Y + r.Height * 0.2f, r.Width, r.Height * 0.8f), radius);
        g.FillPath(brush, tab);
        g.FillPath(brush, body);
    }

    // A filled disc holding a check mark, or an exclamation mark for errors.
    private static void DrawStatusGlyph(Graphics g, RectangleF r, bool isError, float scale)
    {
        using (var disc = new SolidBrush(isError ? ErrorColor : Accent))
            g.FillEllipse(disc, r);

        using var ink = new Pen(GlyphInk, 2.4f * scale);
        ink.StartCap = ink.EndCap = LineCap.Round;
        ink.LineJoin = LineJoin.Round;
        float X(float f) => r.X + r.Width * f;
        float Y(float f) => r.Y + r.Height * f;
        if (!isError)
        {
            g.DrawLines(ink, [new PointF(X(0.28f), Y(0.52f)), new PointF(X(0.44f), Y(0.68f)), new PointF(X(0.73f), Y(0.36f))]);
            return;
        }

        g.DrawLine(ink, X(0.5f), Y(0.26f), X(0.5f), Y(0.56f));
        using var dot = new SolidBrush(GlyphInk);
        var dotSize = 3.2f * scale;
        g.FillEllipse(dot, X(0.5f) - dotSize / 2, Y(0.74f) - dotSize / 2, dotSize, dotSize);
    }

    private void DrawLabel(Graphics g, OverlayLabel? label, Color color)
    {
        if (label is null)
            return;

        var family = label.Font == OverlayFont.Semibold ? TextFamily : NumberFamily;
        DrawText(g, label.Text, label.Bounds, Font(family, label.FontPx, FontStyle.Regular), color, label.Align);
    }

    private void DrawHint(Graphics g, OverlayHint? hint, float scale)
    {
        if (hint is null)
            return;

        var bounds = hint.Bounds;
        var textFont = Font(NumberFamily, hint.FontPx, FontStyle.Regular);
        var capSize = hint.FontPx * 1.4f;
        var gap = hint.FontPx * 0.4f;
        var actionWidth = g.MeasureString(hint.Action, textFont, PointF.Empty, _formats[0]).Width;
        var total = capSize + gap + actionWidth;
        var x = hint.Align switch
        {
            OverlayTextAlign.Near => bounds.X,
            OverlayTextAlign.Center => bounds.X + (bounds.Width - total) / 2,
            _ => bounds.Right - total,
        };

        var cap = new RectangleF(x, bounds.Y + (bounds.Height - capSize) / 2, capSize, capSize);
        using (var path = RoundedRect(cap, capSize * 0.22f))
        {
            using var fill = new SolidBrush(Color.FromArgb(34, 255, 255, 255));
            using var border = new Pen(Color.FromArgb(120, MutedText), scale);
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        DrawText(g, hint.Key, cap, Font(NumberFamily, hint.FontPx, FontStyle.Bold), TextColor, OverlayTextAlign.Center);
        var actionBounds = new RectangleF(cap.Right + gap, bounds.Y, actionWidth + hint.FontPx, bounds.Height);
        DrawText(g, hint.Action, actionBounds, textFont, MutedText, OverlayTextAlign.Near);
    }

    private void DrawText(Graphics g, string text, RectangleF bounds, Font font, Color color, OverlayTextAlign align)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, bounds, _formats[(int)align]);
    }

    private Font Font(string family, float px, FontStyle style)
    {
        var key = (family, MathF.Round(px, 1), style);
        if (!_fonts.TryGetValue(key, out var font))
            _fonts[key] = font = new Font(family, key.Item2, style, GraphicsUnit.Pixel);

        return font;
    }

    private static Color Fade(Color color) => Color.FromArgb(DimmedAlpha * color.A / 255, color);

    private static StringFormat MakeFormat(StringAlignment alignment) => new(StringFormatFlags.NoWrap | StringFormatFlags.LineLimit)
    {
        Alignment = alignment,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(2 * radius, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
