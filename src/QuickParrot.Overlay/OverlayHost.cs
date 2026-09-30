using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Settings;
using static QuickParrot.Overlay.NativeMethods;

namespace QuickParrot.Overlay;

/// <summary>
/// Owns the overlay window and its dedicated STA UI thread. <see cref="Show"/> is safe from any thread,
/// never blocks, and coalesces bursts so only the latest state gets drawn.
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
    private OverlayViewState? _drawn;
    private int _disposed;
    private volatile SmallFolderLayout _smallFolderLayout = SmallFolderLayout.List;

    /// <summary>A user-facing error message. Raised on the overlay thread.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>The overlay window's handle, or 0 before <see cref="Start"/>.</summary>
    public nint WindowHandle => _hwnd;

    /// <summary>How folders with 9 or fewer entries are drawn. Safe to set from any thread; takes effect on the next render.</summary>
    public SmallFolderLayout SmallFolderLayout
    {
        get => _smallFolderLayout;
        set => _smallFolderLayout = value;
    }

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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_thread is not null && _hwnd != 0 && PostMessage(_hwnd, OverlayWindow.WM_QUIT_LOOP, 0, 0))
            _thread.Join(TimeSpan.FromSeconds(5));

        _ready.Dispose();
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

        var state = _pending.Take();
        if (ReferenceEquals(state, _drawn))
            return;

        _drawn = state;
        try
        {
            if (state is null)
                Hide();
            else
                Draw(state);
        }
        catch (Exception e)
        {
            Hide();
            ReportError($"Couldn't draw the overlay: {e.Message}");
        }
    }

    private void Draw(OverlayViewState state)
    {
        if (!_window!.IsShown)
            _monitor = MonitorTarget.ForForegroundWindow();

        var layout = OverlayLayoutGeometry.ComputeForMonitor(state, _monitor.Bounds.Size, _monitor.Dpi, _smallFolderLayout);
        if (_surface?.Size != layout.CanvasSize)
        {
            _surface?.Dispose();
            _surface = null;
            _surface = new LayeredSurface(layout.CanvasSize);
        }

        using (var bitmap = _surface.BeginDraw())
        using (var graphics = Graphics.FromImage(bitmap))
            _renderer!.Draw(graphics, layout);

        _window.ShowAt(_surface, OverlayLayoutGeometry.CenterOn(_monitor.Bounds, layout.CanvasSize));
    }

    // GDI+ startup and JIT cost ~40 ms, which would otherwise land on the first chord press.
    private static void WarmUp(OverlayRenderer renderer)
    {
        var entry = new NumberedEntry(1, OverlayLayoutGeometry.RootTitle, true, "");
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
