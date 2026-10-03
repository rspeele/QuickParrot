namespace QuickParrot.Core.Keyboard;

/// <summary>Abstract input to the chord navigator, produced by mapping physical keys.</summary>
public abstract record ChordEvent;

public sealed record ChordPressed : ChordEvent;

public sealed record SaveNavigationPressed : ChordEvent;

public sealed record ChordReleased : ChordEvent;

/// <summary>Ends the session without a bare-tap stop, e.g. when key tracking is reset mid-chord.</summary>
public sealed record ChordCancelled : ChordEvent;

public sealed record DigitPressed(int Digit, bool Shift) : ChordEvent;

/// <summary>Enter pressed while the chord is held: save the replay buffer.</summary>
public sealed record GrabPressed : ChordEvent;

/// <summary>F1-F12 (<paramref name="Slot"/> 1-12) pressed while the chord is held.</summary>
public sealed record FavoritePressed(int Slot, bool Shift) : ChordEvent;

/// <summary>Delete or Backspace pressed while the chord is held.</summary>
public sealed record FavoriteClearPressed : ChordEvent;

/// <summary>A plain F-key, without the chord, in a fullscreen game: play that favorite outside any session.</summary>
public sealed record ChordlessFavoritePressed(int Slot) : ChordEvent;

public sealed record SearchPressed : ChordEvent;

public sealed record SearchTextEntered(string Text) : ChordEvent;

public sealed record SearchBackspacePressed : ChordEvent;

public sealed record SearchSelectionPressed(int Number) : ChordEvent;

public sealed record SearchKeyPressed(ScanKey Key) : ChordEvent;
