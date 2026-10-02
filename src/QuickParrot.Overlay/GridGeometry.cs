using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class GridGeometry
{
    public const int Rows = 9;

    private const float Padding = 12;
    private const float TitleHeight = 32;
    private const float TitleGap = 4;
    private const float TitleFont = 22;
    private const float HintWidth = 96;
    private const float HintFont = 14;
    private const float HeaderHeight = 28;
    private const float HeaderFont = 22;
    private const float MinColumnWidth = 160;
    private const float ColumnGap = 6;
    private const float CellHeight = 28;
    private const float RowGap = 4;
    private const float CellInset = 4;
    private const float CellCorner = 7;
    private const float IconWidth = 16;
    private const float IconHeight = 13;
    private const float Gap = 6;
    private const float NamePadding = 6;
    private const float NameFont = 13;
    private const float NumberFont = 14;
    private const float HighlightOutset = 3;
    private const float NoteHeight = 26;
    private const float NoteFont = 13;
    private const float MinContentWidth = 440;
    private const float PanelCorner = 12;

    public static OverlayLayout Compute(OverlayViewState state, float scale)
    {
        var columnCount = Math.Max(1, state.GridColumns.Length);
        var gridWidth = columnCount * MinColumnWidth + (columnCount - 1) * ColumnGap;
        var contentWidth = Math.Max(MinContentWidth, gridWidth);
        var columnWidth = (contentWidth - (columnCount - 1) * ColumnGap) / columnCount;
        var headerTop = Padding + TitleHeight + TitleGap;
        var cellsTop = headerTop + HeaderHeight;
        var cellsHeight = Rows * CellHeight + (Rows - 1) * RowGap;
        var noteHeight = state.Truncated ? NoteHeight : 0;
        var height = cellsTop + cellsHeight + noteHeight + Padding;
        var width = contentWidth + 2 * Padding;

        var canvas = new Size((int)MathF.Ceiling(width * scale), (int)MathF.Ceiling(height * scale));
        var panels = new List<OverlayPanel>
        {
            new(new RectangleF(1, 1, canvas.Width - 2, canvas.Height - 2), PanelCorner * scale, OverlayPanelStyle.Panel),
        };

        var headers = new List<OverlayColumnHeader>();
        var items = new List<OverlayItem>();
        foreach (var column in state.GridColumns)
        {
            var zoomed = state.ZoomedColumn == column.Number;
            var dimmed = state.ZoomedColumn is not null && !zoomed;
            var x = Padding + (column.Number - 1) * (columnWidth + ColumnGap);

            if (zoomed)
            {
                var outset = HighlightOutset;
                panels.Add(new OverlayPanel(
                    S(x - outset, headerTop - outset, columnWidth + 2 * outset, HeaderHeight + cellsHeight + 2 * outset, scale),
                    (CellCorner + outset) * scale,
                    OverlayPanelStyle.HighlightedColumn));
            }

            headers.Add(new OverlayColumnHeader(
                column.Number, S(x, headerTop, columnWidth, HeaderHeight, scale), HeaderFont * scale, dimmed));

            foreach (var entry in column.Entries.Take(Rows))
            {
                var y = cellsTop + (entry.Number - 1) * (CellHeight + RowGap);
                items.Add(Cell(entry, x, y, columnWidth, zoomed, dimmed, scale));
            }
        }

        var hintAction = OverlayText.HintActionFor(state);
        var titleWidth = contentWidth - (hintAction is null ? 0 : HintWidth);
        var title = new OverlayLabel(
            OverlayText.TitleFor(state.FolderPath),
            S(Padding, Padding, titleWidth, TitleHeight, scale),
            TitleFont * scale,
            OverlayTextAlign.Near,
            OverlayFont.Semibold);
        var hint = hintAction is null
            ? null
            : new OverlayHint(
                OverlayText.HintKey,
                hintAction,
                S(Padding + contentWidth - HintWidth, Padding, HintWidth, TitleHeight, scale),
                HintFont * scale,
                OverlayTextAlign.Far);
        var note = state.Truncated
            ? new OverlayLabel(
                $"Only the first {state.GridColumns.Sum(c => c.Entries.Length)} entries are shown",
                S(Padding, cellsTop + cellsHeight, contentWidth, NoteHeight, scale),
                NoteFont * scale,
                OverlayTextAlign.Center,
                OverlayFont.Regular)
            : null;

        return new OverlayLayout(
            OverlayLayoutKind.Grid, canvas, scale, panels, headers, items, title, null, hint, note);
    }

    private static OverlayItem Cell(NumberedEntry entry, float x, float y, float width, bool zoomed, bool dimmed, float scale)
    {
        var bounds = S(x, y, width, CellHeight, scale);
        var cursor = x + NamePadding;

        var badge = RectangleF.Empty;
        if (zoomed)
        {
            var size = CellHeight - 2 * CellInset;
            badge = S(x + CellInset, y + CellInset, size, size, scale);
            cursor = x + CellInset + size + Gap;
        }

        var icon = RectangleF.Empty;
        if (entry.IsFolder)
        {
            icon = S(cursor, y + (CellHeight - IconHeight) / 2, IconWidth, IconHeight, scale);
            cursor += IconWidth + Gap;
        }

        var name = S(cursor, y, x + width - NamePadding - cursor, CellHeight, scale);
        return new OverlayItem(
            entry.Number, OverlayText.DisplayName(entry), entry.IsFolder, bounds, CellCorner * scale, badge, icon, name,
            NameFont * scale, NumberFont * scale, zoomed, dimmed);
    }

    private static RectangleF S(float x, float y, float width, float height, float scale) =>
        new(x * scale, y * scale, width * scale, height * scale);
}
