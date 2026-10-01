using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace QuickParrot.App.Editor;

/// <summary>Frozen brushes and pens for <see cref="WaveformView"/>.</summary>
internal static class WaveformPalette
{
    public static readonly Brush Background = Solid(0x17, 0x1A, 0x21);
    public static readonly Brush RulerBackground = Solid(0x22, 0x26, 0x30);
    public static readonly Brush RulerText = Solid(0x9A, 0xA3, 0xB5);
    public static readonly Pen RulerTick = new Pen(Solid(0x4A, 0x52, 0x63), 1).Frozen();
    public static readonly Pen CenterLine = new Pen(Solid(0x2E, 0x34, 0x40), 1).Frozen();
    public static readonly Brush Wave = Solid(0x4B, 0x5D, 0x78);
    public static readonly Brush SelectedWave = Solid(0x5C, 0xC8, 0xFF);
    public static readonly Brush SelectionFill = Solid(0x5C, 0xC8, 0xFF, 0x26);
    public static readonly Brush Handle = Solid(0xE6, 0xF6, 0xFF);
    public static readonly Pen HandleLine = new Pen(Handle, 1.5).Frozen();
    public static readonly Pen CursorLine = new Pen(Solid(0xC8, 0xCE, 0xDA), 1) { DashStyle = new DashStyle([3, 3], 0) }.Frozen();
    public static readonly Brush Playhead = Solid(0xFF, 0x8A, 0x3D);
    public static readonly Pen PlayheadLine = new Pen(Playhead, 1.5).Frozen();

    private static Brush Solid(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen Frozen(this Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
