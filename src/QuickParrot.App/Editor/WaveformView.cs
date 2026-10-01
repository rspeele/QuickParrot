using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QuickParrot.Core.Editing;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace QuickParrot.App.Editor;

/// <summary>
/// Draws a capture's waveform from its peak pyramid with a time ruler, selection, cursor and playhead, and turns mouse
/// input into selection edits (logic in <see cref="SelectionGesture"/>), wheel zoom and Shift+wheel scrolling.
/// </summary>
public sealed class WaveformView : FrameworkElement
{
    private const double RulerHeight = 20;
    private const double MinTickSpacing = 80;
    private const double ZoomPerNotch = 1.25;

    public static readonly DependencyProperty PeaksProperty = Register<WaveformPeaks?>(nameof(Peaks), null, OnPeaksChanged);
    public static readonly DependencyProperty SampleRateProperty = Register(nameof(SampleRate), 48000);
    public static readonly DependencyProperty MinSelectionFramesProperty = Register(nameof(MinSelectionFrames), 0);
    public static readonly DependencyProperty SelectionProperty = Register(nameof(Selection), default(ClipSelection), twoWay: true);
    public static readonly DependencyProperty CursorFrameProperty = Register(nameof(CursorFrame), 0, twoWay: true);
    public static readonly DependencyProperty PlayheadFrameProperty = Register(nameof(PlayheadFrame), double.NaN, OnPlayheadChanged);
    public static readonly DependencyProperty StopMarkerFrameProperty = Register(nameof(StopMarkerFrame), double.NaN);
    public static readonly DependencyProperty ScrollValueProperty = Register(nameof(ScrollValue), 0.0, OnScrollValueChanged, twoWay: true);
    public static readonly DependencyProperty ScrollMaximumProperty = Register(nameof(ScrollMaximum), 0.0);
    public static readonly DependencyProperty VisibleFramesProperty = Register(nameof(VisibleFrames), 0.0);

    public static readonly RoutedEvent SelectionCommittedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectionCommitted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(WaveformView));

    private WaveformViewport _viewport = new(0);
    private SelectionGesture? _gesture;
    private bool _syncingScroll;
    private float[] _mins = [];
    private float[] _maxs = [];

    public WaveformView()
    {
        Focusable = true; // so clicking the waveform takes focus from the name box and Space plays again
        FocusVisualStyle = null;
        ClipToBounds = true;
        SizeChanged += (_, _) => ApplyViewportChange(() => _viewport.SetWidth(ActualWidth));
    }

    /// <summary>Raised when a drag that changed the selection ends.</summary>
    public event RoutedEventHandler SelectionCommitted
    {
        add => AddHandler(SelectionCommittedEvent, value);
        remove => RemoveHandler(SelectionCommittedEvent, value);
    }

    public WaveformPeaks? Peaks { get => (WaveformPeaks?)GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }

    public int SampleRate { get => (int)GetValue(SampleRateProperty); set => SetValue(SampleRateProperty, value); }

    public int MinSelectionFrames { get => (int)GetValue(MinSelectionFramesProperty); set => SetValue(MinSelectionFramesProperty, value); }

    public ClipSelection Selection { get => (ClipSelection)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }

    public int CursorFrame { get => (int)GetValue(CursorFrameProperty); set => SetValue(CursorFrameProperty, value); }

    /// <summary>The frame being heard during preview, or NaN to hide the playhead.</summary>
    public double PlayheadFrame { get => (double)GetValue(PlayheadFrameProperty); set => SetValue(PlayheadFrameProperty, value); }

    /// <summary>Where playback last stopped manually, or NaN to hide the marker.</summary>
    public double StopMarkerFrame { get => (double)GetValue(StopMarkerFrameProperty); set => SetValue(StopMarkerFrameProperty, value); }

    /// <summary>First visible frame; bind a horizontal ScrollBar's Value here (with Maximum and ViewportSize below).</summary>
    public double ScrollValue { get => (double)GetValue(ScrollValueProperty); set => SetValue(ScrollValueProperty, value); }

    public double ScrollMaximum { get => (double)GetValue(ScrollMaximumProperty); private set => SetValue(ScrollMaximumProperty, value); }

    public double VisibleFrames { get => (double)GetValue(VisibleFramesProperty); private set => SetValue(VisibleFramesProperty, value); }

    public void ShowAll() => ApplyViewportChange(_viewport.ShowAll);

    /// <summary>Zooms so the selection fills most of the view.</summary>
    public void ZoomToSelection()
    {
        var selection = Selection;
        if (selection.Length <= 0)
            return;

        ApplyViewportChange(() =>
        {
            var margin = selection.Length * 0.1;
            _viewport.ShowAll();
            _viewport.ZoomAround(0, _viewport.VisibleFrames / (selection.Length + 2 * margin));
            _viewport.ScrollTo(selection.Start - margin);
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(WaveformPalette.Background, null, new Rect(0, 0, width, height));
        if (Peaks is not { } peaks || width <= 0 || height <= RulerHeight)
            return;

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var waveTop = RulerHeight;
        var waveHeight = height - RulerHeight;
        var mid = waveTop + waveHeight / 2;
        var selection = Selection;
        double selectionLeft = _viewport.XAt(selection.Start), selectionRight = _viewport.XAt(selection.End);
        var hasSelection = selection.Length > 0;

        if (hasSelection)
            dc.DrawRectangle(WaveformPalette.SelectionFill, null, VisibleSpan(selectionLeft, selectionRight, waveTop, waveHeight));

        dc.DrawLine(WaveformPalette.CenterLine, new Point(0, mid), new Point(width, mid));
        var wave = BuildWaveGeometry(peaks, width, scale, mid, waveHeight / 2 - 2);
        dc.DrawGeometry(WaveformPalette.Wave, null, wave);
        if (hasSelection)
        {
            dc.PushClip(new RectangleGeometry(VisibleSpan(selectionLeft, selectionRight, waveTop, waveHeight)));
            dc.DrawGeometry(WaveformPalette.SelectedWave, null, wave);
            dc.Pop();
            DrawHandle(dc, selectionLeft, waveTop, height, pointsRight: true);
            DrawHandle(dc, selectionRight, waveTop, height, pointsRight: false);
        }

        var cursorX = _viewport.XAt(CursorFrame);
        dc.DrawLine(WaveformPalette.CursorLine, new Point(cursorX, waveTop), new Point(cursorX, height));
        DrawRuler(dc, width, scale);

        if (!double.IsNaN(PlayheadFrame))
        {
            var x = _viewport.XAt(PlayheadFrame);
            dc.DrawLine(WaveformPalette.PlayheadLine, new Point(x, 0), new Point(x, height));
            dc.DrawGeometry(WaveformPalette.Playhead, null, Triangle(x, RulerHeight - 8, 5, pointsDown: true));
        }

        if (!double.IsNaN(StopMarkerFrame))
        {
            var x = _viewport.XAt(StopMarkerFrame);
            dc.DrawLine(WaveformPalette.StopMarkerLine, new Point(x, waveTop), new Point(x, height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Peaks is null)
            return;

        Focus();
        CaptureMouse();
        _gesture = SelectionGesture.Begin(_viewport, Selection, e.GetPosition(this).X, MinSelectionFrames);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        if (_gesture is null)
        {
            Cursor = SelectionGesture.HitTest(_viewport, Selection, x) == SelectionDragTarget.NewSelection ? Cursors.IBeam : Cursors.SizeWE;
            return;
        }

        if (x < 0 || x > ActualWidth)
            ApplyViewportChange(() => _viewport.ScrollBy((x < 0 ? x : x - ActualWidth) * 0.5)); // drag past an edge scrolls

        if (_gesture.Move(x) is { } selection)
            Selection = selection;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_gesture is not { } gesture)
            return;

        _gesture = null;
        ReleaseMouseCapture();
        if (gesture.IsClick)
            CursorFrame = gesture.ClickFrame;
        else
            RaiseEvent(new SelectionCommittedEventArgs(SelectionCommittedEvent, this, gesture.Target, gesture.Anchor));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_gesture is { IsClick: false } gesture)
            RaiseEvent(new SelectionCommittedEventArgs(SelectionCommittedEvent, this, gesture.Target, gesture.Anchor));

        _gesture = null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var x = e.GetPosition(this).X;
        var notches = e.Delta / 120.0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            ApplyViewportChange(() => _viewport.ScrollBy(-notches * ActualWidth / 8));
        else
            ApplyViewportChange(() => _viewport.ZoomAround(x, Math.Pow(ZoomPerNotch, notches)));

        e.Handled = true;
    }

    private Geometry BuildWaveGeometry(WaveformPeaks peaks, double width, double scale, double mid, double halfHeight)
    {
        var columns = (int)Math.Ceiling(width * scale);
        if (_mins.Length != columns)
        {
            _mins = new float[columns];
            _maxs = new float[columns];
        }

        peaks.GetColumns(_viewport.FirstFrame, _viewport.FramesPerPixel / scale, _mins, _maxs);
        var minHeight = 1 / scale;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < columns; i++)
            {
                if (float.IsNaN(_mins[i]))
                    continue;

                var top = mid - Math.Clamp(_maxs[i], -1f, 1f) * halfHeight;
                var bottom = mid - Math.Clamp(_mins[i], -1f, 1f) * halfHeight;
                if (bottom - top < minHeight)
                    (top, bottom) = ((top + bottom - minHeight) / 2, (top + bottom + minHeight) / 2);

                double left = i / scale, right = (i + 1) / scale;
                ctx.BeginFigure(new Point(left, top), isFilled: true, isClosed: true);
                ctx.PolyLineTo([new Point(right, top), new Point(right, bottom), new Point(left, bottom)], isStroked: false, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private void DrawRuler(DrawingContext dc, double width, double scale)
    {
        dc.DrawRectangle(WaveformPalette.RulerBackground, null, new Rect(0, 0, width, RulerHeight));
        var sampleRate = Math.Max(1, SampleRate);
        var step = TimeFormatting.RulerStep(_viewport.FramesPerPixel / sampleRate, MinTickSpacing);
        var firstSecond = _viewport.FirstFrame / sampleRate;
        var lastSecond = _viewport.FrameAt(width) / sampleRate;
        for (var tick = Math.Ceiling(firstSecond / step) * step; tick <= lastSecond; tick += step)
        {
            var x = Math.Round(_viewport.XAt(tick * sampleRate) * scale) / scale + 0.5 / scale;
            dc.DrawLine(WaveformPalette.RulerTick, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));
            var label = new FormattedText(TimeFormatting.Tick(tick, step), CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10.5, WaveformPalette.RulerText, scale);
            dc.DrawText(label, new Point(x + 3, 2));
        }
    }

    private static void DrawHandle(DrawingContext dc, double x, double top, double bottom, bool pointsRight)
    {
        dc.DrawLine(WaveformPalette.HandleLine, new Point(x, top), new Point(x, bottom));
        var tab = pointsRight ? new Rect(x, top, 7, 12) : new Rect(x - 7, top, 7, 12);
        dc.DrawRectangle(WaveformPalette.Handle, null, tab);
    }

    private static Geometry Triangle(double x, double top, double half, bool pointsDown)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var tip = pointsDown ? top + 2 * half : top;
            var bas = pointsDown ? top : top + 2 * half;
            ctx.BeginFigure(new Point(x - half, bas), isFilled: true, isClosed: true);
            ctx.PolyLineTo([new Point(x + half, bas), new Point(x, tip)], isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private Rect VisibleSpan(double left, double right, double top, double height)
    {
        left = Math.Clamp(left, -1, ActualWidth + 1);
        right = Math.Clamp(right, -1, ActualWidth + 1);
        return new Rect(left, top, Math.Max(0, right - left), height);
    }

    private void ApplyViewportChange(Action change)
    {
        change();
        _syncingScroll = true;
        try
        {
            ScrollMaximum = _viewport.MaxFirstFrame;
            VisibleFrames = _viewport.VisibleFrames;
            ScrollValue = _viewport.FirstFrame;
        }
        finally
        {
            _syncingScroll = false;
        }

        InvalidateVisual();
    }

    private static void OnPeaksChanged(WaveformView view, WaveformPeaks? peaks) =>
        view.ApplyViewportChange(() => view._viewport = new WaveformViewport(peaks?.FrameCount ?? 0, view.ActualWidth));

    private static void OnScrollValueChanged(WaveformView view, double value)
    {
        if (!view._syncingScroll)
            view.ApplyViewportChange(() => view._viewport.ScrollTo(value));
    }

    // While playing, page the view along so the playhead stays visible.
    private static void OnPlayheadChanged(WaveformView view, double frame)
    {
        if (!double.IsNaN(frame) && view._gesture is null && view._viewport.IsZoomedIn)
            view.ApplyViewportChange(() => view._viewport.EnsureVisible(frame));
    }

    private static DependencyProperty Register<T>(string name, T defaultValue, Action<WaveformView, T>? changed = null, bool twoWay = false)
    {
        var options = FrameworkPropertyMetadataOptions.AffectsRender
            | (twoWay ? FrameworkPropertyMetadataOptions.BindsTwoWayByDefault : FrameworkPropertyMetadataOptions.None);
        PropertyChangedCallback? callback = changed is null ? null : (d, e) => changed((WaveformView)d, (T)e.NewValue);
        return DependencyProperty.Register(name, typeof(T), typeof(WaveformView), new FrameworkPropertyMetadata(defaultValue, options, callback));
    }
}

/// <summary>A finished selection drag: which handle moved (or <see cref="SelectionDragTarget.NewSelection"/>) and its anchor frame.</summary>
public sealed class SelectionCommittedEventArgs(RoutedEvent routedEvent, object source, SelectionDragTarget target, int anchorFrame)
    : RoutedEventArgs(routedEvent, source)
{
    public SelectionDragTarget Target { get; } = target;

    public int AnchorFrame { get; } = anchorFrame;
}
