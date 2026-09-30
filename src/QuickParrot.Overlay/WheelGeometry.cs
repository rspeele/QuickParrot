using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class WheelGeometry
{
    private const float RadiusX = 420;
    private const float RadiusY = 240;

    public static OverlayLayout Compute(OverlayViewState state, float scale)
    {
        var canvas = new Size(
            (int)MathF.Ceiling(2 * (RadiusX + PillGeometry.PillWidth / 2 + PillGeometry.Margin) * scale),
            (int)MathF.Ceiling(2 * (RadiusY + PillGeometry.PillHeight / 2 + PillGeometry.Margin) * scale));
        var center = new PointF(canvas.Width / 2f, canvas.Height / 2f);

        var items = new List<OverlayItem>(state.WheelEntries.Count);
        for (var i = 0; i < state.WheelEntries.Count; i++)
        {
            var angle = 2 * MathF.PI * i / state.WheelEntries.Count;
            var pillCenter = new PointF(
                center.X + RadiusX * scale * MathF.Sin(angle),
                center.Y - RadiusY * scale * MathF.Cos(angle));
            items.Add(PillGeometry.Pill(state.WheelEntries[i], pillCenter, scale));
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

    private static (OverlayPanel, OverlayLabel, OverlayLabel?, OverlayLabel?) CenterLabels(
        OverlayViewState state, PointF center, float scale)
    {
        var panelHeight = (HeaderGeometry.ContentHeight(state) + 2 * HeaderGeometry.Padding) * scale;
        var panelBounds = new RectangleF(
            center.X - HeaderGeometry.Width * scale / 2, center.Y - panelHeight / 2, HeaderGeometry.Width * scale, panelHeight);

        var (title, subtitle, hint) = HeaderGeometry.Build(state, panelBounds, scale);
        return (new OverlayPanel(panelBounds, 16 * scale, OverlayPanelStyle.Panel), title, subtitle, hint);
    }
}
