namespace QuickParrot.Core.Navigation;

/// <summary>Effect emitted by <see cref="ChordNavigator"/> for the audio layer to execute.</summary>
public abstract record NavigationAction;

public sealed record PlayClip(string RelativePath) : NavigationAction;

public sealed record StopPlayback : NavigationAction;

public sealed record GrabReplay : NavigationAction;
