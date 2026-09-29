namespace QuickParrot.Core.Navigation;

/// <summary>Abstract input to <see cref="ChordNavigator"/>, produced by mapping physical keys upstream.</summary>
public abstract record ChordEvent;

public sealed record ChordPressed : ChordEvent;

public sealed record ChordReleased : ChordEvent;

public sealed record DigitPressed(int Digit, bool Shift) : ChordEvent;
