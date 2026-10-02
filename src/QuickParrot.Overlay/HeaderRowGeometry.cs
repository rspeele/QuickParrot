using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class HeaderRowGeometry
{
    private const float TitleFont = 22;
    private const float SaveHintWidth = 130;
    private const float SaveHintFont = 11;
    private const float HintWidth = 60;
    private const float HintFont = 14;
    private const float Gap = 6;

    public static (OverlayLabel Title, OverlayHint? Hint, OverlaySaveNavigationHint? SaveHint) Build(
        OverlayViewState state, RectangleF bounds, float scale)
    {
        var titleWidth = Math.Max(0, bounds.Width - (SaveHintWidth + HintWidth + 2 * Gap) * scale);
        var titleBounds = bounds with { Width = titleWidth };
        var saveBounds = bounds with { X = titleBounds.Right + Gap * scale, Width = SaveHintWidth * scale };
        var hintBounds = bounds with { X = bounds.Right - HintWidth * scale, Width = HintWidth * scale };

        var title = new OverlayLabel(
            OverlayText.TitleFor(state.FolderPath), titleBounds, TitleFont * scale, OverlayTextAlign.Near, OverlayFont.Semibold);
        var hint = OverlayText.HintActionFor(state) is { } action
            ? new OverlayHint(OverlayText.HintKey, action, hintBounds, HintFont * scale, OverlayTextAlign.Far)
            : null;
        var saveHint = state.ShowSaveNavigationHint && state.Favorites is null
            ? new OverlaySaveNavigationHint(
                new OverlayLabel("Shift: Save Navigation", saveBounds, SaveHintFont * scale, OverlayTextAlign.Center,
                    OverlayFont.Regular), state.ShiftHeld)
            : null;

        return (title, hint, saveHint);
    }
}
