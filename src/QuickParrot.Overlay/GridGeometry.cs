using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class GridGeometry
{
    public const int Rows = 9;

    private const float Padding = 18;
    private const float TitleHeight = 40;
    private const float TitleGap = 8;
    private const float TitleFont = 24;
    private const float HintWidth = 150;
    private const float HintFont = 16;
    private const float HeaderHeight = 36;
    private const float HeaderFont = 24;
    private const float ColumnWidth = 160;
    private const float ColumnGap = 8;
    private const float CellHeight = 32;
    private const float RowGap = 4;
    private const float CellInset = 4;
    private const float CellCorner = 7;
    private const float IconWidth = 16;
    private const float IconHeight = 13;
    private const float Gap = 6;
    private const float NamePadding = 8;
    private const float NameFont = 14;
    private const float NumberFont = 15;
    private const float HighlightOutset = 4;
    private const float NoteHeight = 26;
    private const float NoteFont = 13;
    private const float MinContentWidth = 440;
    private const float PanelCorner = 14;

    public static OverlayLayout Compute(OverlayViewState state, float scale)
    {
        var columnCount = Math.Max(1, state.GridColumns.Count);
        var gridWidth = columnCount * ColumnWidth + (columnCount - 1) * ColumnGap;
        var contentWidth = Math.Max(MinContentWidth, gridWidth);
        var gridLeft = Padding + (contentWidth - gridWidth) / 2;
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
            var x = gridLeft + (column.Number - 1) * (ColumnWidth + ColumnGap);

            if (zoomed)
            {
                var outset = HighlightOutset;
                panels.Add(new OverlayPanel(
                    S(x - outset, headerTop - outset, ColumnWidth + 2 * outset, HeaderHeight + cellsHeight + 2 * outset, scale),
                    (CellCorner + outset) * scale,
                    OverlayPanelStyle.HighlightedColumn));
            }

            headers.Add(new OverlayColumnHeader(
                column.Number, S(x, headerTop, ColumnWidth, HeaderHeight, scale), HeaderFont * scale, dimmed));

            foreach (var entry in column.Entries.Take(Rows))
            {
                var y = cellsTop + (entry.Number - 1) * (CellHeight + RowGap);
                items.Add(Cell(entry, x, y, zoomed, dimmed, scale));
            }
        }

        var hintText = OverlayLayoutGeometry.HintFor(state);
        var titleWidth = contentWidth - (hintText is null ? 0 : HintWidth);
        var title = new OverlayLabel(
            OverlayLayoutGeometry.TitleFor(state.FolderPath),
            S(Padding, Padding, titleWidth, TitleHeight, scale),
            TitleFont * scale,
            OverlayTextAlign.Near);
        var hint = hintText is null
            ? null
            : new OverlayLabel(
                hintText,
                S(Padding + contentWidth - HintWidth, Padding, HintWidth, TitleHeight, scale),
                HintFont * scale,
                OverlayTextAlign.Far);
        var note = state.Truncated
            ? new OverlayLabel(
                $"Only the first {state.GridColumns.Sum(c => c.Entries.Count)} entries are shown",
                S(Padding, cellsTop + cellsHeight, contentWidth, NoteHeight, scale),
                NoteFont * scale,
                OverlayTextAlign.Center)
            : null;

        return new OverlayLayout(
            OverlayLayoutKind.Grid, canvas, scale, panels, headers, items, title, null, hint, note);
    }

    private static OverlayItem Cell(NumberedEntry entry, float x, float y, bool zoomed, bool dimmed, float scale)
    {
        var bounds = S(x, y, ColumnWidth, CellHeight, scale);
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

        var name = S(cursor, y, x + ColumnWidth - NamePadding - cursor, CellHeight, scale);
        return new OverlayItem(
            entry.Number, OverlayLayoutGeometry.DisplayName(entry), entry.IsFolder, bounds, CellCorner * scale, badge, icon, name,
            NameFont * scale, NumberFont * scale, zoomed, dimmed);
    }

    private static RectangleF S(float x, float y, float width, float height, float scale) =>
        new(x * scale, y * scale, width * scale, height * scale);
}
