using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

internal static class SaveNavigationHintGeometry
{
    public const float Height = 20;
    private const float Font = 12;

    public static float HeightFor(OverlayViewState state) =>
        state.ShowSaveNavigationHint && state.Favorites is null ? Height : 0;

    public static OverlaySaveNavigationHint? Build(
        OverlayViewState state, RectangleF bounds, float scale, OverlayTextAlign align) =>
        HeightFor(state) == 0 ? null : new OverlaySaveNavigationHint(
            new OverlayLabel("Shift: Save Navigation", bounds, Font * scale, align, OverlayFont.Regular), state.ShiftHeld);
}
