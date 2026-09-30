using System.Drawing;
using System.Runtime.InteropServices;
using static QuickParrot.Overlay.NativeMethods;

namespace QuickParrot.Overlay;

/// <summary>A monitor's bounds in physical pixels and its effective DPI.</summary>
internal readonly record struct MonitorTarget(Rectangle Bounds, int Dpi)
{
    /// <summary>The monitor holding the foreground window (the game), else the primary monitor.</summary>
    public static MonitorTarget ForForegroundWindow()
    {
        var foreground = GetForegroundWindow();
        var monitor = foreground != 0
            ? MonitorFromWindow(foreground, MONITOR_DEFAULTTOPRIMARY)
            : MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);

        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("Couldn't find a monitor to show the overlay on.");

        var r = info.Monitor;
        var dpi = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? (int)dpiX : 96;
        return new MonitorTarget(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), dpi);
    }
}
