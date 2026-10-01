using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>The folder title / empty-subtitle / hint block, shared by the ring and list layouts.</summary>
internal static class HeaderGeometry
{
    public const float Width = 360;
    public const float Padding = 14;
    private const float TitleFont = 25;
    private const float TitleHeight = 36;
    private const float SubtitleFont = 18;
    private const float SubtitleHeight = 28;
    private const float HintFont = 16;
    private const float HintHeight = 28;

    public static float ContentHeight(OverlayViewState state) =>
        TitleHeight
        + (state.WheelEntries.Count == 0 ? SubtitleHeight : 0)
        + (OverlayLayoutGeometry.HintFor(state) is null ? 0 : HintHeight);

    public static (OverlayLabel Title, OverlayLabel? Subtitle, OverlayLabel? Hint) Build(
        OverlayViewState state, RectangleF panelBounds, float scale)
    {
        var textX = panelBounds.X + Padding * scale;
        var textWidth = panelBounds.Width - 2 * Padding * scale;
        var y = panelBounds.Y + Padding * scale;

        OverlayLabel Line(string text, float lineHeight, float font)
        {
            var label = new OverlayLabel(
                text, new RectangleF(textX, y, textWidth, lineHeight * scale), font * scale, OverlayTextAlign.Center);
            y += lineHeight * scale;
            return label;
        }

        var title = Line(OverlayLayoutGeometry.TitleFor(state.FolderPath), TitleHeight, TitleFont);
        var subtitle = state.WheelEntries.Count == 0 ? Line(OverlayLayoutGeometry.EmptyLabel, SubtitleHeight, SubtitleFont) : null;
        var hintText = OverlayLayoutGeometry.HintFor(state);
        var hint = hintText is null ? null : Line(hintText, HintHeight, HintFont);

        return (title, subtitle, hint);
    }
}
