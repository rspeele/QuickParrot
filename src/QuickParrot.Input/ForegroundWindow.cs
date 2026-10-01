using QuickParrot.Core.Favorites;
using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

internal static unsafe class ForegroundWindow
{
    private static readonly uint s_processId = (uint)Environment.ProcessId;

    /// <summary>Whether the focused window looks like a fullscreen or borderless game. Allocation-free.</summary>
    public static bool IsFullscreenGame()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || !GetWindowRect(hwnd, out var window))
            return false;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info))
            return false;

        var classBuffer = stackalloc char[64];
        var classLength = Math.Max(0, GetClassNameW(hwnd, classBuffer, 64));
        GetWindowThreadProcessId(hwnd, out var processId);
        var hasTitleBar = (GetWindowLongW(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;

        return FullscreenRule.IsFullscreenGame(
            ToPixelRect(window), ToPixelRect(info.rcMonitor), new ReadOnlySpan<char>(classBuffer, classLength),
            ownWindow: processId == s_processId, hasTitleBar);
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
