using System.Drawing;

namespace QuickParrot.Overlay;

/// <summary>A one-line confirmation pill: status icon then text.</summary>
public sealed record ToastLayout(
    Size CanvasSize, float Scale, OverlayPanel Panel, RectangleF IconBounds, OverlayLabel Text, bool IsError);

/// <summary>Pure toast geometry. Text width is estimated, not measured, and the renderer ellipsizes any overflow.</summary>
public static class ToastGeometry
{
    public const float Height = 52;
    public const float MinTextWidth = 120;
    public const float MaxTextWidth = 560;

    /// <summary>Where the toast's top edge sits, as a fraction of the monitor height: clear of the crosshair.</summary>
    public const float TopFraction = 0.08f;

    private const float Margin = 2; // room for the antialiased border
    private const float Padding = 18;
    private const float IconSize = 24;
    private const float Gap = 12;
    private const float FontPx = 18;
    private const float AverageCharWidth = 0.52f * FontPx;

    public static ToastLayout Compute(string text, float scale, bool isError = false)
    {
        var textWidth = Math.Clamp(text.Length * AverageCharWidth, MinTextWidth, MaxTextWidth);
        var panelWidth = (Padding + IconSize + Gap + textWidth + Padding) * scale;
        var panelHeight = Height * scale;
        var margin = Margin * scale;
        var canvas = new Size((int)MathF.Ceiling(panelWidth + 2 * margin), (int)MathF.Ceiling(panelHeight + 2 * margin));

        var panelBounds = new RectangleF(
            (canvas.Width - panelWidth) / 2, (canvas.Height - panelHeight) / 2, panelWidth, panelHeight);
        var iconSize = IconSize * scale;
        var icon = new RectangleF(
            panelBounds.X + Padding * scale, panelBounds.Y + (panelHeight - iconSize) / 2, iconSize, iconSize);
        var textX = icon.Right + Gap * scale;
        var textBounds = new RectangleF(textX, panelBounds.Y, panelBounds.Right - Padding * scale - textX, panelHeight);
        var label = new OverlayLabel(text, textBounds, FontPx * scale, OverlayTextAlign.Near);

        var panel = new OverlayPanel(panelBounds, panelHeight / 2, OverlayPanelStyle.Panel);
        return new ToastLayout(canvas, scale, panel, icon, label, isError);
    }

    /// <summary>Lays out for a monitor (physical pixels, effective DPI) at the same scale the overlay would use.</summary>
    public static ToastLayout ComputeForMonitor(string text, Size monitorSize, int dpi, bool isError = false)
    {
        var baseSize = Compute(text, 1, isError).CanvasSize;
        return Compute(text, OverlayLayoutGeometry.ChooseScale(monitorSize, dpi, baseSize), isError);
    }

    public static Point PositionOn(Rectangle monitor, Size canvas) =>
        new(monitor.X + (monitor.Width - canvas.Width) / 2, monitor.Y + (int)(monitor.Height * TopFraction));
}
