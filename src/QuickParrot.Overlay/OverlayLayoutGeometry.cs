using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>Pure overlay geometry: turns a view state into pixel rectangles for the renderer. No GDI.</summary>
public static class OverlayLayoutGeometry
{
    /// <summary>Design sizes are for a 1080p monitor at 100% DPI; scale 2 is 4K.</summary>
    private const float ReferenceMonitorHeight = 1080;

    private const float MaxMonitorFraction = 0.92f;
    private const float DpiScaleWeight = 0.85f;

    public static OverlayLayout Compute(OverlayViewState state, float scale) =>
        state.Favorites is { } favorites
            ? FavoritesGeometry.Compute(favorites, ComputeFolder(state, scale), scale)
            : ComputeFolder(state, scale);

    private static OverlayLayout ComputeFolder(OverlayViewState state, float scale) =>
        state.Layout != OverlayLayoutKind.Wheel ? GridGeometry.Compute(state, scale)
        : state.SmallFolderLayout == SmallFolderLayout.Ring ? WheelGeometry.Compute(state, scale)
        : ListGeometry.Compute(state, scale);

    /// <summary>Lays out for a monitor (physical pixels, effective DPI), scaled to look alike at any resolution.</summary>
    public static OverlayLayout ComputeForMonitor(OverlayViewState state, Size monitorSize, int dpi)
    {
        var baseSize = Compute(state, 1).CanvasSize;
        return Compute(state, ChooseScale(monitorSize, dpi, baseSize));
    }

    // Tracks monitor height, but small high-DPI screens (laptops) get a bit bigger; never overflows the monitor.
    public static float ChooseScale(Size monitorSize, int dpi, Size baseCanvasSize)
    {
        var desired = Math.Max(monitorSize.Height / ReferenceMonitorHeight, dpi / 96f * DpiScaleWeight);
        var fitWidth = MaxMonitorFraction * monitorSize.Width / Math.Max(1, baseCanvasSize.Width);
        var fitHeight = MaxMonitorFraction * monitorSize.Height / Math.Max(1, baseCanvasSize.Height);
        return Math.Max(0.1f, Math.Min(desired, Math.Min(fitWidth, fitHeight)));
    }

    public static Point CenterOn(Rectangle monitor, Size canvas) =>
        new(monitor.X + (monitor.Width - canvas.Width) / 2, monitor.Y + (monitor.Height - canvas.Height) / 2);
}
