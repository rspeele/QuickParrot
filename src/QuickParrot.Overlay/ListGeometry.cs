using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>Small folders shown as a single top-to-bottom column: a header above, item 1 at the top.</summary>
internal static class ListGeometry
{
    private const float HeaderGap = 14;
    private const float RowGap = 10;

    public static OverlayLayout Compute(OverlayViewState state, float scale)
    {
        var count = state.WheelEntries.Count;
        var contentWidth = Math.Max(HeaderGeometry.Width, PillGeometry.PillWidth);
        var headerHeight = HeaderGeometry.ContentHeight(state) + 2 * HeaderGeometry.Padding;
        var listHeight = count == 0 ? 0 : count * PillGeometry.PillHeight + (count - 1) * RowGap;

        var width = (contentWidth + 2 * PillGeometry.Margin) * scale;
        var height = (PillGeometry.Margin + headerHeight + (count == 0 ? 0 : HeaderGap + listHeight) + PillGeometry.Margin) * scale;
        var canvas = new Size((int)MathF.Ceiling(width), (int)MathF.Ceiling(height));
        var centerX = canvas.Width / 2f;

        var panelBounds = new RectangleF(
            centerX - HeaderGeometry.Width * scale / 2, PillGeometry.Margin * scale, HeaderGeometry.Width * scale, headerHeight * scale);
        var (title, subtitle, hint) = HeaderGeometry.Build(state, panelBounds, scale);
        var panel = new OverlayPanel(panelBounds, 16 * scale, OverlayPanelStyle.Panel);

        var items = new List<OverlayItem>(count);
        var y = panelBounds.Bottom + HeaderGap * scale;
        foreach (var entry in state.WheelEntries)
        {
            var pillCenter = new PointF(centerX, y + PillGeometry.PillHeight * scale / 2);
            items.Add(PillGeometry.Pill(entry, pillCenter, scale));
            y += (PillGeometry.PillHeight + RowGap) * scale;
        }

        return new OverlayLayout(OverlayLayoutKind.Wheel, canvas, scale, [panel], [], items, title, subtitle, hint, null);
    }
}
