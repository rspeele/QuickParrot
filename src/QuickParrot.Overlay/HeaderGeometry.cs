using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>The folder title / empty-subtitle / hint block, shared by the ring and list layouts.</summary>
internal static class HeaderGeometry
{
    public const float Width = 360;
    public const float Padding = 14;
    private const float TitleHeight = 36;
    private const float SubtitleFont = 18;
    private const float SubtitleHeight = 28;

    public static float ContentHeight(OverlayViewState state) =>
        TitleHeight
        + (state.WheelEntries.Length == 0 ? SubtitleHeight : 0);

    public static (OverlayLabel Title, OverlayLabel? Subtitle, OverlayHint? Hint, OverlaySaveNavigationHint? SaveHint) Build(
        OverlayViewState state, RectangleF panelBounds, float scale)
    {
        var textX = panelBounds.X + Padding * scale;
        var textWidth = panelBounds.Width - 2 * Padding * scale;
        var y = panelBounds.Y + Padding * scale;

        RectangleF Line(float lineHeight)
        {
            var bounds = new RectangleF(textX, y, textWidth, lineHeight * scale);
            y += lineHeight * scale;
            return bounds;
        }

        var (title, hint, saveHint) = HeaderRowGeometry.Build(state, Line(TitleHeight), scale);
        var subtitle = state.WheelEntries.Length == 0
            ? new OverlayLabel(
                OverlayText.EmptyLabel, Line(SubtitleHeight), SubtitleFont * scale, OverlayTextAlign.Center, OverlayFont.Regular)
            : null;
        return (title, subtitle, hint, saveHint);
    }
}
