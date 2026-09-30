using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

public enum OverlayTextAlign
{
    Near,
    Center,
    Far,
}

public enum OverlayPanelStyle
{
    Panel,
    HighlightedColumn,
}

public sealed record OverlayPanel(RectangleF Bounds, float CornerRadius, OverlayPanelStyle Style);

public sealed record OverlayLabel(string Text, RectangleF Bounds, float FontPx, OverlayTextAlign Align);

/// <summary>A wheel pill or grid cell. Empty sub-rectangles mean "don't draw that part".</summary>
public sealed record OverlayItem(
    int Number,
    string Name,
    bool IsFolder,
    RectangleF Bounds,
    float CornerRadius,
    RectangleF NumberBounds,
    RectangleF IconBounds,
    RectangleF NameBounds,
    float NameFontPx,
    float NumberFontPx,
    bool Highlighted,
    bool Dimmed);

public sealed record OverlayColumnHeader(int Number, RectangleF Bounds, float FontPx, bool Highlighted, bool Dimmed);

/// <summary>Everything the renderer draws, in canvas pixels. Drawn in order: panels, headers, items, labels.</summary>
public sealed record OverlayLayout(
    OverlayLayoutKind Kind,
    Size CanvasSize,
    float Scale,
    IReadOnlyList<OverlayPanel> Panels,
    IReadOnlyList<OverlayColumnHeader> Headers,
    IReadOnlyList<OverlayItem> Items,
    OverlayLabel Title,
    OverlayLabel? Subtitle,
    OverlayLabel? Hint,
    OverlayLabel? Note);
