using QuickParrot.Core.Favorites;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>Assign mode: the favorites strip across the top, with the usual folder layout centered below it.</summary>
internal static class FavoritesGeometry
{
    public const float SlotWidth = 104;
    public const float SlotHeight = 50;
    public const float SlotGap = 6;
    public const int MaxLastPlayedChars = 32;
    public const string EmptyName = "empty";
    public const string MissingTag = "missing";

    private const float Margin = 2; // room for the antialiased border
    private const float Padding = 14;
    private const float TitleHeight = 30;
    private const float TitleFont = 20;
    private const float InstructionsHeight = 24;
    private const float InstructionsFont = 14;
    private const float SlotsTop = 10;
    private const float StripGap = 12;
    private const float SlotInset = 8;
    private const float KeyRowTop = 4;
    private const float KeyRowHeight = 20;
    private const float KeyWidth = 36;
    private const float NameRowHeight = 22;
    private const float KeyFont = 14;
    private const float TagFont = 11;
    private const float NameFont = 13;
    private const float SlotCorner = 8;
    private const float PanelCorner = 14;

    public static float StripWidth => FavoriteSlots.Count * SlotWidth + (FavoriteSlots.Count - 1) * SlotGap + 2 * Padding;

    public static float StripHeight => Padding + TitleHeight + InstructionsHeight + SlotsTop + SlotHeight + Padding;

    public static OverlayLayout Compute(FavoritesPanel favorites, OverlayLayout folder, float scale)
    {
        var stripWidth = StripWidth * scale;
        var top = (Margin + StripHeight + StripGap) * scale;
        var canvas = new Size(
            Math.Max((int)MathF.Ceiling(stripWidth + 2 * Margin * scale), folder.CanvasSize.Width),
            (int)MathF.Ceiling(top) + folder.CanvasSize.Height);

        var stripBounds = new RectangleF((canvas.Width - stripWidth) / 2, Margin * scale, stripWidth, StripHeight * scale);
        var strip = BuildStrip(favorites, stripBounds, scale);
        var folderOrigin = new PointF((canvas.Width - folder.CanvasSize.Width) / 2f, MathF.Ceiling(top));
        return folder with { CanvasSize = canvas, Favorites = strip, FolderOrigin = folderOrigin };
    }

    public static string TitleFor(FavoritesPanel favorites) => $"Assigning {FavoriteSlots.KeyName(favorites.TargetSlot)}";

    public static string InstructionsFor(FavoritesPanel favorites)
    {
        var key = FavoriteSlots.KeyName(favorites.TargetSlot);
        var chord = favorites.ChordKeyName.Length > 0 ? favorites.ChordKeyName : "the chord key";
        var lastPlayed = favorites.LastPlayedName is { } name ? $" · {key}: last played ({Truncate(name)})" : "";
        return $"Pick a clip{lastPlayed} · Del: clear · Release {chord} to cancel";
    }

    public static string Truncate(string text) =>
        text.Length <= MaxLastPlayedChars ? text : text[..(MaxLastPlayedChars - 1)].TrimEnd() + "…";

    private static FavoritesStrip BuildStrip(FavoritesPanel favorites, RectangleF bounds, float scale)
    {
        var x = bounds.X + Padding * scale;
        var y = bounds.Y + Padding * scale;
        var width = bounds.Width - 2 * Padding * scale;
        var title = new OverlayLabel(
            TitleFor(favorites), new RectangleF(x, y, width, TitleHeight * scale), TitleFont * scale, OverlayTextAlign.Center,
            OverlayFont.Semibold);
        y += TitleHeight * scale;
        var instructions = new OverlayLabel(
            InstructionsFor(favorites), new RectangleF(x, y, width, InstructionsHeight * scale), InstructionsFont * scale,
            OverlayTextAlign.Center, OverlayFont.Regular);
        y += (InstructionsHeight + SlotsTop) * scale;

        var slots = new List<FavoriteSlotItem>(favorites.Slots.Length);
        foreach (var slot in favorites.Slots)
        {
            var slotX = x + (slot.Slot - 1) * (SlotWidth + SlotGap) * scale;
            slots.Add(Slot(slot, slot.Slot == favorites.TargetSlot, slotX, y, scale));
        }

        return new FavoritesStrip(new OverlayPanel(bounds, PanelCorner * scale, OverlayPanelStyle.Panel), title, instructions, slots);
    }

    private static FavoriteSlotItem Slot(FavoriteSlotView view, bool target, float x, float y, float scale)
    {
        var state = view.UnavailableReason is not null ? FavoriteSlotState.Unavailable
            : view.IsEmpty ? FavoriteSlotState.Empty
            : view.Missing ? FavoriteSlotState.Missing
            : FavoriteSlotState.Assigned;
        var tag = state == FavoriteSlotState.Missing ? MissingTag : null;
        var name = view.UnavailableReason is { } reason ? $"({reason})" : view.Name ?? EmptyName;

        var inset = SlotInset * scale;
        var bounds = new RectangleF(x, y, SlotWidth * scale, SlotHeight * scale);
        var keyBounds = new RectangleF(x + inset, y + KeyRowTop * scale, KeyWidth * scale, KeyRowHeight * scale);
        var tagBounds = tag is null
            ? RectangleF.Empty
            : new RectangleF(keyBounds.Right, keyBounds.Y, bounds.Right - inset - keyBounds.Right, keyBounds.Height);
        var nameBounds = new RectangleF(x + inset, keyBounds.Bottom, bounds.Width - 2 * inset, NameRowHeight * scale);

        return new FavoriteSlotItem(
            view.KeyName, name, tag, state, target, bounds, SlotCorner * scale, keyBounds,
            tagBounds, nameBounds, KeyFont * scale, TagFont * scale, NameFont * scale);
    }
}
