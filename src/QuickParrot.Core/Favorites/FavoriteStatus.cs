using System.Collections.Immutable;
using System.Runtime.InteropServices;
using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Favorites;

/// <summary>One favorite slot as shown to the user.</summary>
/// <param name="Name">The clip's display name, or null if the slot is empty.</param>
/// <param name="Missing">The slot names a clip that isn't in the library (moved, deleted, or a different library).</param>
/// <param name="UnavailableReason">Set when the F-key is taken by another binding, so it can't trigger the slot.</param>
public sealed record FavoriteSlotView(int Slot, string? Name, bool Missing, string? UnavailableReason)
{
    public string KeyName => FavoriteSlots.KeyName(Slot);

    public bool IsEmpty => Name is null;

    /// <summary>For the desktop app's favorites list, e.g. "Wilhelm scream (missing)".</summary>
    public string DisplayText
    {
        get
        {
            var text = Name is null ? "(empty)" : Missing ? $"{Name} (missing)" : Name;
            return UnavailableReason is null ? text : $"{text} ({KeyName} is the {UnavailableReason})";
        }
    }
}

public static class FavoriteStatus
{
    /// <param name="clipExists">Whether a relative path is a clip in the current library; null if there's no library.</param>
    public static ImmutableArray<FavoriteSlotView> Describe(
        FavoriteSlots favorites, Func<string, bool>? clipExists, ScanKey chordKey, PushToTalkBinding pushToTalk)
    {
        var views = new FavoriteSlotView[FavoriteSlots.Count];
        for (var slot = 1; slot <= FavoriteSlots.Count; slot++)
        {
            var path = favorites[slot];
            var missing = path is not null && clipExists?.Invoke(path) != true;
            views[slot - 1] = new FavoriteSlotView(
                slot, path is null ? null : FavoriteSlots.DisplayName(path), missing,
                UnavailableReason(slot, chordKey, pushToTalk));
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(views);
    }

    /// <summary>
    /// Why slot's F-key can't trigger it, or null if it can. The chord key always wins, and the push-to-talk key is
    /// left alone so the game keeps hearing it.
    /// </summary>
    public static string? UnavailableReason(int slot, ScanKey chordKey, PushToTalkBinding pushToTalk)
    {
        var key = ScanKey.ForFunctionKey(slot);
        return key == chordKey ? "chord key"
            : key == pushToTalk.Key ? "PTT key"
            : null;
    }

    /// <summary>Bit (slot - 1) set for each F-key that plays its favorite without the chord.</summary>
    public static int ChordlessMask(FavoriteSlots favorites, bool favoritesWithoutChord) =>
        favoritesWithoutChord ? favorites.AssignedMask : 0;
}

/// <summary>A favorites confirmation or problem, worth a brief toast.</summary>
public sealed record FavoriteNotice(string Message, bool IsError)
{
    public static FavoriteNotice Assigned(int slot, string relativePath) =>
        new($"{FavoriteSlots.KeyName(slot)} → {FavoriteSlots.DisplayName(relativePath)}", false);

    public static FavoriteNotice Cleared(int slot) => new($"{FavoriteSlots.KeyName(slot)} cleared", false);

    public static FavoriteNotice AlreadyEmpty(int slot) => new($"{FavoriteSlots.KeyName(slot)} is already empty", false);

    public static FavoriteNotice SlotEmpty(int slot) => new($"{FavoriteSlots.KeyName(slot)} is empty", true);

    public static FavoriteNotice SlotMissing(int slot) =>
        new($"{FavoriteSlots.KeyName(slot)}'s clip was moved or deleted", true);

    public static readonly FavoriteNotice NothingPlayed = new("Nothing has been played yet", true);

    public static readonly FavoriteNotice LastPlayedMissing = new("The last-played clip was moved or deleted", true);

    public static readonly FavoriteNotice NotAClip = new("Only clips in the sound library can be favorites", true);

    public static readonly FavoriteNotice NoLibrary = new("Choose a sound library first", true);
}
