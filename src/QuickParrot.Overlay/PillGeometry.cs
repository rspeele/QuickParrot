using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>A numbered pill (badge, optional folder glyph, name), shared by the ring and list layouts.</summary>
internal static class PillGeometry
{
    public const float PillWidth = 270;
    public const float PillHeight = 48;
    public const float Margin = 14;
    private const float BadgeInset = 5;
    private const float IconWidth = 20;
    private const float IconHeight = 16;
    private const float Gap = 8;
    private const float NameEndPadding = 18;
    private const float NameFont = 17;
    private const float NumberFont = 20;

    public static OverlayItem Pill(
        NumberedEntry entry, PointF pillCenter, float scale,
        float width = PillWidth, float height = PillHeight, float nameFont = NameFont, float numberFont = NumberFont)
    {
        var scaledHeight = height * scale;
        var bounds = new RectangleF(pillCenter.X - width * scale / 2, pillCenter.Y - scaledHeight / 2, width * scale, scaledHeight);

        var inset = BadgeInset * scale;
        var badgeSize = scaledHeight - 2 * inset;
        var badge = new RectangleF(bounds.X + inset, bounds.Y + inset, badgeSize, badgeSize);
        var x = badge.Right + Gap * scale;

        var icon = RectangleF.Empty;
        if (entry.IsFolder)
        {
            icon = new RectangleF(x, pillCenter.Y - IconHeight * scale / 2, IconWidth * scale, IconHeight * scale);
            x = icon.Right + Gap * scale;
        }

        var name = new RectangleF(x, bounds.Y, bounds.Right - NameEndPadding * scale - x, scaledHeight);
        return new OverlayItem(
            entry.Number, OverlayText.DisplayName(entry), entry.IsFolder, bounds, scaledHeight / 2, badge, icon, name,
            nameFont * scale, numberFont * scale, Highlighted: false, Dimmed: false);
    }
}
