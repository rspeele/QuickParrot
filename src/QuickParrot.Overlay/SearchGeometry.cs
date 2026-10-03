using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class SearchGeometry
{
    private const float Width = 500;
    private const float Margin = 18;
    private const float HeaderHeight = 120;
    private const float RowHeight = 40;
    private const float Gap = 6;

    public static OverlayLayout Compute(OverlayViewState state, float scale)
    {
        var canvas = new Size((int)MathF.Ceiling((Width + 2 * Margin) * scale),
            (int)MathF.Ceiling((2 * Margin + HeaderHeight + 9 * (RowHeight + Gap)) * scale));
        var header = new RectangleF(Margin * scale, Margin * scale, Width * scale, HeaderHeight * scale);
        OverlayLabel Label(string text, float top, float height, float font, OverlayFont weight) =>
            new(text, new RectangleF((Margin + 14) * scale, (Margin + top) * scale,
                (Width - 28) * scale, height * scale), font * scale, OverlayTextAlign.Near, weight);

        var title = Label("Search library", 10, 28, 18, OverlayFont.Semibold);
        var query = Label(state.SearchQuery!.Length == 0 ? "Type a clip name…" : state.SearchQuery + "▏",
            40, 36, 22, OverlayFont.Regular);
        var instructions = Label(state.WheelEntries.IsEmpty
            ? "No matches · Esc cancels" : "1–9 play · Enter plays 1 · Esc cancels", 84, 26, 14, OverlayFont.Regular);
        var items = state.WheelEntries.Select((entry, index) => PillGeometry.Pill(entry,
            new PointF(canvas.Width / 2f, (Margin + HeaderHeight + Gap + index * (RowHeight + Gap) + RowHeight / 2) * scale),
            scale, Width, RowHeight, 15, 18)).ToArray();
        return new OverlayLayout(OverlayLayoutKind.Wheel, canvas, scale,
            [new OverlayPanel(header, 16 * scale, OverlayPanelStyle.Panel)], [], items, title, query, null, instructions);
    }
}
