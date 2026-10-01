using System.Drawing.Imaging;
using QuickParrot.Core.Navigation;
using static QuickParrot.Overlay.NativeMethods;

namespace QuickParrot.Overlay;

/// <summary>
/// Owns the overlay window and its dedicated STA UI thread. <see cref="Show"/> and <see cref="ShowToast"/> are safe
/// from any thread, never block, and coalesce bursts so only the latest state gets drawn.
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private OverlayWindow? _window;
    private OverlayRenderer? _renderer;
    private LayeredSurface? _surface;
    private MonitorTarget _monitor;
    private Exception? _startupError;
    private readonly LatestValueSlot<OverlayViewState> _pending = new();
    private volatile nint _hwnd;
    private object? _drawn; // the view state or toast on screen, or null
    private ToastRequest? _toast;
    private System.Threading.Timer? _toastTimer; // overlay thread only
    private int _disposed;

    /// <summary>A user-facing error message. Raised on the overlay thread.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Starts the overlay thread and waits until its (hidden) window exists.</summary>
    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("The overlay is already started.");

        _thread = new Thread(Run) { IsBackground = true, Name = "QuickParrot overlay" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
        if (_startupError is not null)
            throw new InvalidOperationException("Couldn't create the overlay window.", _startupError);

        if (_pending.RequestWake())
            Wake(); // delivers anything shown before Start
    }

    /// <summary>Shows or updates the overlay; null hides it.</summary>
    public void Show(OverlayViewState? state)
    {
        if (_pending.Set(state))
            Wake();
    }

    /// <summary>
    /// Shows a one-line confirmation near the top of the screen for <paramref name="duration"/>, replacing any earlier
    /// one. While a chord session is showing, that takes precedence and the toast waits underneath until it expires.
    /// </summary>
    public void ShowToast(string text, TimeSpan duration, bool isError = false)
    {
        Volatile.Write(ref _toast, ToastRequest.Create(text, isError, duration, Environment.TickCount64));
        RequestRedraw();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_thread is not null && _hwnd != 0 && PostMessage(_hwnd, OverlayWindow.WM_QUIT_LOOP, 0, 0))
            _thread.Join(TimeSpan.FromSeconds(5));

        _ready.Dispose();
    }

    private void RequestRedraw()
    {
        if (_pending.RequestWake())
            Wake();
    }

    private void Wake()
    {
        var delivered = _hwnd != 0 && Volatile.Read(ref _disposed) == 0 && PostMessage(_hwnd, OverlayWindow.WM_APPLY, 0, 0);
        if (!delivered)
            _pending.CancelWake();
    }

    private void Run()
    {
        try
        {
            // Never let WinForms show its exception dialog, which would steal focus from the game.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
            // Makes this thread per-monitor-v2 aware even if the process manifest isn't.
            SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            _renderer = new OverlayRenderer();
            WarmUp(_renderer);
            _window = new OverlayWindow(OnAppMessage, OnWindowError);
            _toastTimer = new System.Threading.Timer(_ => RequestRedraw());
            _hwnd = _window.Handle;
        }
        catch (Exception e)
        {
            _startupError = e;
            _renderer?.Dispose();
        }
        finally
        {
            _ready.Set();
        }

        if (_startupError is not null)
            return;

        try
        {
            Application.Run();
        }
        catch (Exception e)
        {
            ReportError($"The overlay stopped working: {e.Message}");
        }

        _hwnd = 0;
        try
        {
            _toastTimer?.Dispose();
            _window?.Hide();
            _window?.DestroyHandle();
            _surface?.Dispose();
            _renderer?.Dispose();
        }
        catch
        {
            // Teardown is best effort; throwing here would crash the process.
        }
    }

    private void OnWindowError(Exception e)
    {
        try
        {
            Hide();
        }
        catch
        {
            // Already reporting a failure; the next draw will try again.
        }

        _drawn = null;
        ReportError($"Overlay error: {e.Message}");
    }

    private void ReportError(string message)
    {
        try
        {
            ErrorOccurred?.Invoke(message);
        }
        catch
        {
            // A broken handler mustn't take the overlay thread down with it.
        }
    }

    private void OnAppMessage(int message)
    {
        if (message == OverlayWindow.WM_QUIT_LOOP)
        {
            Application.ExitThread();
            return;
        }

        // Read after Take, so a toast set concurrently is either seen now or wakes us again.
        var now = Environment.TickCount64;
        var state = _pending.Take();
        var toast = Volatile.Read(ref _toast);
        // Re-armed on every wake, so a timer firing slightly early just wakes us again.
        if (toast is not null && toast.IsLive(now))
            _toastTimer!.Change(TimeSpan.FromMilliseconds(toast.ExpiresAt - now), Timeout.InfiniteTimeSpan);

        var scene = ToastRequest.ChooseScene(state, toast, now);
        if (ReferenceEquals(scene, _drawn))
            return;

        _drawn = scene;
        try
        {
            if (scene is OverlayViewState viewState)
                Draw(viewState);
            else if (scene is ToastRequest toastRequest)
                DrawToast(toastRequest);
            else
                Hide();
        }
        catch (Exception e)
        {
            Hide();
            ReportError($"Couldn't draw the overlay: {e.Message}");
        }
    }

    private void Draw(OverlayViewState state)
    {
        UpdateMonitor();
        var layout = OverlayLayoutGeometry.ComputeForMonitor(state, _monitor.Bounds.Size, _monitor.Dpi);
        var topLeft = OverlayLayoutGeometry.CenterOn(_monitor.Bounds, layout.CanvasSize);
        Present(layout.CanvasSize, topLeft, g => _renderer!.Draw(g, layout));
    }

    private void DrawToast(ToastRequest toast)
    {
        UpdateMonitor();
        var layout = ToastGeometry.ComputeForMonitor(toast.Text, _monitor.Bounds.Size, _monitor.Dpi, toast.IsError);
        var topLeft = ToastGeometry.PositionOn(_monitor.Bounds, layout.CanvasSize);
        Present(layout.CanvasSize, topLeft, g => _renderer!.DrawToast(g, layout));
    }

    // Follows the game to another monitor, but never jumps while something is already showing.
    private void UpdateMonitor()
    {
        if (!_window!.IsShown)
            _monitor = MonitorTarget.ForForegroundWindow();
    }

    private void Present(Size canvasSize, Point topLeft, Action<Graphics> draw)
    {
        if (_surface?.Size != canvasSize)
        {
            _surface?.Dispose();
            _surface = null;
            _surface = new LayeredSurface(canvasSize);
        }

        using (var bitmap = _surface.BeginDraw())
        using (var graphics = Graphics.FromImage(bitmap))
            draw(graphics);

        _window!.ShowAt(_surface, topLeft);
    }

    // GDI+ startup and JIT cost ~40 ms, which would otherwise land on the first chord press.
    private static void WarmUp(OverlayRenderer renderer)
    {
        var entry = new NumberedEntry(1, OverlayText.RootTitle, true);
        var layout = OverlayLayoutGeometry.Compute(new OverlayViewState("", OverlayLayoutKind.Wheel, [entry], [], null, false), 0.25f);
        using var bitmap = new Bitmap(layout.CanvasSize.Width, layout.CanvasSize.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        renderer.Draw(graphics, layout);
    }

    // Frees the surface too, so an idle overlay holds no pixel memory.
    private void Hide()
    {
        _window?.Hide();
        _surface?.Dispose();
        _surface = null;
    }
}
