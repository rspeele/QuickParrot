namespace QuickParrot.Core.Navigation;

/// <summary>Abstract input to <see cref="ChordNavigator"/>, produced by mapping physical keys upstream.</summary>
public abstract record ChordEvent;

public sealed record ChordPressed : ChordEvent;

public sealed record ChordReleased : ChordEvent;

/// <summary>Ends the session without a bare-tap stop, e.g. when key tracking is reset mid-chord.</summary>
public sealed record ChordCancelled : ChordEvent;

public sealed record DigitPressed(int Digit, bool Shift) : ChordEvent;

/// <summary>Enter pressed while the chord is held: save the replay buffer.</summary>
public sealed record GrabPressed : ChordEvent;
