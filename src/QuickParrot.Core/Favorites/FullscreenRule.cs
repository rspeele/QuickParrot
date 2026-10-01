namespace QuickParrot.Core.Favorites;

/// <summary>A screen rectangle in physical pixels; right and bottom are exclusive, as in Win32.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;
}

/// <summary>
/// Decides whether the focused window looks like a fullscreen or borderless game, so plain F-keys can play favorites
/// there without stealing them from browsers and file managers. Allocation-free: it runs inside the keyboard hook.
/// </summary>
public static class FullscreenRule
{
    internal static bool CoversMonitor(PixelRect window, PixelRect monitor) =>
        !monitor.IsEmpty && window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    /// <param name="ownWindow">The window belongs to QuickParrot itself.</param>
    /// <param name="hasTitleBar">An ordinary app window, which can cover the monitor when maximized without a taskbar.</param>
    public static bool IsFullscreenGame(
        PixelRect window, PixelRect monitor, ReadOnlySpan<char> className, bool ownWindow, bool hasTitleBar) =>
        !ownWindow && !hasTitleBar && !IsShellWindow(className) && CoversMonitor(window, monitor);

    /// <summary>The desktop and taskbar, which cover the screen without being a game.</summary>
    private static bool IsShellWindow(ReadOnlySpan<char> className) =>
        className.SequenceEqual("Progman") || className.SequenceEqual("WorkerW")
        || className.SequenceEqual("Shell_TrayWnd") || className.SequenceEqual("Shell_SecondaryTrayWnd");
}
