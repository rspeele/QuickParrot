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

public enum FavoriteSlotState
{
    Assigned,
    Empty,
    Missing,
    Unavailable,
}

/// <summary>One cell of the favorites strip: the F-key label, an optional tag beside it, and the clip name below.</summary>
public sealed record FavoriteSlotItem(
    int Slot,
    string KeyLabel,
    string Name,
    string? Tag,
    FavoriteSlotState State,
    bool Target,
    RectangleF Bounds,
    float CornerRadius,
    RectangleF KeyBounds,
    RectangleF TagBounds,
    RectangleF NameBounds,
    float KeyFontPx,
    float TagFontPx,
    float NameFontPx);

/// <summary>The assign-mode panel above the folder view: a title, instructions and the twelve slots.</summary>
public sealed record FavoritesStrip(
    OverlayPanel Panel, OverlayLabel Title, OverlayLabel Instructions, IReadOnlyList<FavoriteSlotItem> Slots);

/// <summary>
/// Everything the renderer draws, in canvas pixels. Drawn in order: favorites strip, panels, headers, items, labels.
/// </summary>
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
    OverlayLabel? Note,
    FavoritesStrip? Favorites = null);
