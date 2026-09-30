using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class WheelGeometry
{
    private const float RadiusX = 420;
    private const float RadiusY = 240;
    private const float PillWidth = 270;
    private const float PillHeight = 48;
    private const float Margin = 14;
    private const float BadgeInset = 5;
    private const float IconWidth = 20;
    private const float IconHeight = 16;
    private const float Gap = 8;
    private const float NameEndPadding = 18;
    private const float NameFont = 17;
    private const float NumberFont = 20;

    private const float CenterWidth = 360;
    private const float CenterPadding = 14;
    private const float TitleFont = 25;
    private const float TitleHeight = 36;
    private const float SubtitleFont = 18;
    private const float SubtitleHeight = 28;
    private const float HintFont = 16;
    private const float HintHeight = 28;

    public static OverlayLayout Compute(OverlayViewState state, float scale)
    {
        var canvas = new Size(
            (int)MathF.Ceiling(2 * (RadiusX + PillWidth / 2 + Margin) * scale),
            (int)MathF.Ceiling(2 * (RadiusY + PillHeight / 2 + Margin) * scale));
        var center = new PointF(canvas.Width / 2f, canvas.Height / 2f);

        var items = new List<OverlayItem>(state.WheelEntries.Count);
        for (var i = 0; i < state.WheelEntries.Count; i++)
        {
            var angle = 2 * MathF.PI * i / state.WheelEntries.Count;
            var pillCenter = new PointF(
                center.X + RadiusX * scale * MathF.Sin(angle),
                center.Y - RadiusY * scale * MathF.Cos(angle));
            items.Add(Pill(state.WheelEntries[i], pillCenter, scale));
        }

        var (panel, title, subtitle, hint) = CenterLabels(state, center, scale);
        return new OverlayLayout(
            OverlayLayoutKind.Wheel,
            canvas,
            scale,
            [panel],
            [],
            items,
            title,
            subtitle,
            hint,
            null);
    }

    private static OverlayItem Pill(NumberedEntry entry, PointF pillCenter, float scale)
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
            entry.Number, OverlayLayoutGeometry.DisplayName(entry), entry.IsFolder, bounds, height / 2, badge, icon, name,
            NameFont * scale, NumberFont * scale, Highlighted: false, Dimmed: false);
    }

    private static (OverlayPanel, OverlayLabel, OverlayLabel?, OverlayLabel?) CenterLabels(
        OverlayViewState state, PointF center, float scale)
    {
        var subtitleText = state.WheelEntries.Count == 0 ? OverlayLayoutGeometry.EmptyLabel : null;
        var hintText = OverlayLayoutGeometry.HintFor(state);

        var contentHeight = TitleHeight
            + (subtitleText is null ? 0 : SubtitleHeight)
            + (hintText is null ? 0 : HintHeight);
        var panelHeight = (contentHeight + 2 * CenterPadding) * scale;
        var panel = new RectangleF(
            center.X - CenterWidth * scale / 2, center.Y - panelHeight / 2, CenterWidth * scale, panelHeight);

        var textX = panel.X + CenterPadding * scale;
        var textWidth = panel.Width - 2 * CenterPadding * scale;
        var y = panel.Y + CenterPadding * scale;

        OverlayLabel Line(string text, float lineHeight, float font)
        {
            var label = new OverlayLabel(
                text, new RectangleF(textX, y, textWidth, lineHeight * scale), font * scale, OverlayTextAlign.Center);
            y += lineHeight * scale;
            return label;
        }

        var title = Line(OverlayLayoutGeometry.TitleFor(state.FolderPath), TitleHeight, TitleFont);
        var subtitle = subtitleText is null ? null : Line(subtitleText, SubtitleHeight, SubtitleFont);
        var hint = hintText is null ? null : Line(hintText, HintHeight, HintFont);

        return (new OverlayPanel(panel, 16 * scale, OverlayPanelStyle.Panel), title, subtitle, hint);
    }
}
