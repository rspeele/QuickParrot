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

    public static OverlayItem Pill(NumberedEntry entry, PointF pillCenter, float scale)
    {
        var height = PillHeight * scale;
        var bounds = new RectangleF(pillCenter.X - PillWidth * scale / 2, pillCenter.Y - height / 2, PillWidth * scale, height);

        var inset = BadgeInset * scale;
        var badgeSize = height - 2 * inset;
        var badge = new RectangleF(bounds.X + inset, bounds.Y + inset, badgeSize, badgeSize);
        var x = badge.Right + Gap * scale;

        var icon = RectangleF.Empty;
        if (entry.IsFolder)
        {
            icon = new RectangleF(x, pillCenter.Y - IconHeight * scale / 2, IconWidth * scale, IconHeight * scale);
            x = icon.Right + Gap * scale;
        }

        var name = new RectangleF(x, bounds.Y, bounds.Right - NameEndPadding * scale - x, height);
        return new OverlayItem(
            entry.Number, OverlayText.DisplayName(entry), entry.IsFolder, bounds, height / 2, badge, icon, name,
            NameFont * scale, NumberFont * scale, Highlighted: false, Dimmed: false);
    }
}
