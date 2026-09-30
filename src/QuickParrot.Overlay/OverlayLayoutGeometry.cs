using System.Drawing;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Settings;

namespace QuickParrot.Overlay;

/// <summary>Pure overlay geometry: turns a view state into pixel rectangles for the renderer. No GDI.</summary>
public static class OverlayLayoutGeometry
{
    public const string RootTitle = "QuickParrot";
    public const string UpHint = "0 · Up";
    public const string BackHint = "0 · Back";
    public const string EmptyLabel = "(empty)";

    /// <summary>Design sizes are for a 1080p monitor at 100% DPI; scale 2 is 4K.</summary>
    public const float ReferenceMonitorHeight = 1080;

    private const float MaxMonitorFraction = 0.92f;
    private const float DpiScaleWeight = 0.85f;

    public static OverlayLayout Compute(
        OverlayViewState state, float scale, SmallFolderLayout smallFolderLayout = SmallFolderLayout.List) =>
        state.Layout != OverlayLayoutKind.Wheel ? GridGeometry.Compute(state, scale)
        : smallFolderLayout == SmallFolderLayout.Ring ? WheelGeometry.Compute(state, scale)
        : ListGeometry.Compute(state, scale);

    /// <summary>Lays out for a monitor (physical pixels, effective DPI), scaled to look alike at any resolution.</summary>
    public static OverlayLayout ComputeForMonitor(
        OverlayViewState state, Size monitorSize, int dpi, SmallFolderLayout smallFolderLayout = SmallFolderLayout.List)
    {
        var baseSize = Compute(state, 1, smallFolderLayout).CanvasSize;
        return Compute(state, ChooseScale(monitorSize, dpi, baseSize), smallFolderLayout);
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

    internal static string TitleFor(string folderPath)
    {
        var separator = folderPath.LastIndexOf('/');
        return folderPath.Length == 0 ? RootTitle : folderPath[(separator + 1)..];
    }

    internal static string DisplayName(NumberedEntry entry)
    {
        var name = entry.IsFolder ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
        return name.Length > 0 ? name : entry.Name;
    }

    internal static string? HintFor(OverlayViewState state) =>
        state.ZoomedColumn is not null ? BackHint
        : state.FolderPath.Length > 0 ? UpHint
        : null;
}
