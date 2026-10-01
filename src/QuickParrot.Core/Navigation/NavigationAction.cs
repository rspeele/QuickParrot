namespace QuickParrot.Core.Navigation;

/// <summary>Effect emitted by <see cref="ChordNavigator"/> for the engine to execute.</summary>
public abstract record NavigationAction;

public sealed record PlayClip(string RelativePath) : NavigationAction;

public sealed record StopPlayback : NavigationAction;

public sealed record GrabReplay : NavigationAction;

public sealed record PlayFavorite(int Slot) : NavigationAction;

public sealed record AssignFavorite(int Slot, string RelativePath) : NavigationAction;

public sealed record AssignLastPlayedFavorite(int Slot) : NavigationAction;

public sealed record ClearFavorite(int Slot) : NavigationAction;

/// <summary><see cref="ChordNavigator.PersistentPath"/> changed to <paramref name="Path"/>; save it.</summary>
public sealed record PersistPath(string Path) : NavigationAction;

/// <summary>Build the strip for <paramref name="Slot"/> and pass it to <see cref="ChordNavigator.ShowFavorites"/>;
/// emitted when assign mode needs a strip it doesn't have yet.</summary>
public sealed record NeedFavoritesPanel(int Slot) : NavigationAction;
