using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class SearchGeometry
{
    private const float Width = 1000;
    private const float Margin = 18;
    private const float HeaderHeight = 154;
    private const float RowHeight = 44;
    private const float Gap = 6;
    private const float NameLeft = 43;
    private const float NameWidth = 690;
    private const float TopFolderLeft = 745;
    private const float ParentFolderLeft = 879;
    private const float TopFolderWidth = 122;
    private const float ParentFolderWidth = 103;
    private static readonly Color[] FolderColors =
    [
        Color.FromArgb(164, 207, 176), Color.FromArgb(151, 208, 235),
        Color.FromArgb(195, 177, 240), Color.FromArgb(237, 171, 204),
        Color.FromArgb(242, 187, 151), Color.FromArgb(232, 215, 139),
        Color.FromArgb(147, 218, 209), Color.FromArgb(181, 195, 242),
    ];

    public static OverlayLayout Compute(OverlayViewState state, float scale)
    {
        var fragments = state.IsFragmentSearch;
        var headerHeight = fragments ? 276 : HeaderHeight;
        var canvas = new Size((int)MathF.Ceiling((Width + 2 * Margin) * scale),
            (int)MathF.Ceiling((2 * Margin + headerHeight + 9 * (RowHeight + Gap)) * scale));
        var header = new RectangleF(Margin * scale, Margin * scale, Width * scale, headerHeight * scale);
        OverlayLabel Label(string text, float top, float height, float font, OverlayFont weight) =>
            new(text, new RectangleF((Margin + 14) * scale, (Margin + top) * scale,
                (Width - 28) * scale, height * scale), font * scale, OverlayTextAlign.Near, weight);

        var title = Label(fragments
            ? $"Compose phrase · {state.FragmentSpeaker ?? "All speakers"} · {state.FragmentNames.Length} fragments" +
                (state.FragmentNames.Length > 9 ? $" ({state.FragmentNames.Length - 9} earlier)" : "")
            : "Search library", 10, 28, 18, OverlayFont.Semibold);
        var query = Label(state.SearchQuery!.Length == 0 ? "Type a clip or folder name…" : state.SearchQuery + "▏",
            fragments ? 145 : 40, 36, 22, OverlayFont.Regular);
        var instructions = Label(fragments
            ? (state.WheelEntries.IsEmpty ? "No matches · " : "Tap Enter or 1–9 to add · ") + "Hold Enter 0.5s to play · Esc cancels"
            : state.WheelEntries.IsEmpty ? "No matches · Esc cancels" : "1–9 play · Enter plays 1 · Esc cancels",
            fragments ? 186 : 84, 26, 14, OverlayFont.Regular);
        var items = state.WheelEntries.Select((entry, index) => SearchItem(entry,
            new PointF((Margin + Width / 2) * scale, (Margin + headerHeight + Gap + index * (RowHeight + Gap) + RowHeight / 2) * scale),
            scale)).ToArray();
        return new OverlayLayout(OverlayLayoutKind.Wheel, canvas, scale,
            [new OverlayPanel(header, 16 * scale, OverlayPanelStyle.Panel)], [], items, title, query, null, instructions)
        {
            ColumnLabels =
            [
                ColumnLabel("Clip", NameLeft, NameWidth),
                ColumnLabel(fragments ? "Speaker" : "Top folder", TopFolderLeft, TopFolderWidth),
                ColumnLabel("Parent folder", ParentFolderLeft, ParentFolderWidth),
            ],
            PhraseLabels = fragments ? PhraseLabels(state, scale).Append(
                Label("Backspace twice with an empty search removes the last fragment", 212, 24, 13, OverlayFont.Regular)).ToArray() : [],
            HoldProgressBounds = fragments && state.FragmentHoldProgress > 0
                ? new RectangleF(header.Left + 14 * scale, header.Bottom - 5 * scale,
                    (Width - 28) * scale * (float)Math.Clamp(state.FragmentHoldProgress, 0, 1), 3 * scale)
                : RectangleF.Empty,
        };

        OverlayLabel ColumnLabel(string text, float left, float width) =>
            new(text, new RectangleF((Margin + left) * scale, (Margin + (fragments ? 242 : 124)) * scale,
                width * scale, 24 * scale), 13 * scale, OverlayTextAlign.Near, OverlayFont.Regular);
    }

    private static IEnumerable<OverlayLabel> PhraseLabels(OverlayViewState state, float scale)
    {
        if (state.FragmentNames.IsEmpty)
        {
            yield return new OverlayLabel("Search and add your first fragment to choose a speaker",
                new RectangleF((Margin + 14) * scale, (Margin + 42) * scale, (Width - 28) * scale, 28 * scale),
                16 * scale, OverlayTextAlign.Near, OverlayFont.Regular);
            yield break;
        }

        var names = state.FragmentNames.TakeLast(9).ToArray();
        for (var index = 0; index < names.Length; index++)
        {
            var number = state.FragmentNames.Length - names.Length + index + 1;
            yield return new OverlayLabel($"{number}. {names[index]}",
                new RectangleF((Margin + 14 + index % 3 * 326) * scale,
                    (Margin + 42 + index / 3 * 32) * scale, 314 * scale, 28 * scale),
                16 * scale, OverlayTextAlign.Near, OverlayFont.Semibold);
        }
    }

    private static OverlayItem SearchItem(NumberedEntry entry, PointF center, float scale)
    {
        var bounds = new RectangleF(center.X - Width * scale / 2, center.Y - RowHeight * scale / 2,
            Width * scale, RowHeight * scale);
        var badge = new RectangleF(bounds.X + 5 * scale, center.Y - 15 * scale, 30 * scale, 30 * scale);
        var name = new RectangleF(bounds.X + NameLeft * scale, bounds.Y, NameWidth * scale, bounds.Height);
        return new OverlayItem(entry.Number, OverlayText.DisplayName(entry), false, bounds, RowHeight * scale / 2,
            badge, RectangleF.Empty, name, 15 * scale, 18 * scale, Highlighted: false, Dimmed: false)
        {
            TopFolder = FolderLabel(entry.FolderContext?.TopFolder ?? "", TopFolderLeft, TopFolderWidth, 14, OverlayFont.Semibold),
            ParentFolder = FolderLabel(entry.FolderContext?.ParentFolder ?? "", ParentFolderLeft, ParentFolderWidth, 12, OverlayFont.Regular),
            TopFolderColor = entry.FolderContext?.ColorIndex is int color && color >= 0 && color < FolderColors.Length
                ? FolderColors[color] : null,
        };

        OverlayLabel FolderLabel(string text, float left, float width, float font, OverlayFont weight) =>
            new(text, new RectangleF(bounds.X + left * scale, bounds.Y, width * scale, bounds.Height),
                font * scale, OverlayTextAlign.Near, weight);
    }
}
