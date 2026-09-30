using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Playback;

/// <summary>What to inject for the push-to-talk binding.</summary>
public enum PushToTalkSend
{
    Nothing,
    Down,
    Up,
}

/// <summary>
/// Makes the game see one push-to-talk input that's down while the user physically holds it or QuickParrot
/// holds it. Decides what to inject and which physical events to hide. Not thread-safe, never allocates.
/// </summary>
public sealed class PushToTalkMerger
{
    public PushToTalkMerger(PushToTalkBinding binding)
    {
        Binding = binding;
    }

    public PushToTalkBinding Binding { get; private set; }

    /// <summary>QuickParrot is holding the binding for a clip.</summary>
    public bool Holding { get; private set; }

    /// <summary>The user is physically holding the binding, as far as is known.</summary>
    public bool PhysicallyHeld { get; private set; }

    /// <summary>Whether the game should currently see the binding as down.</summary>
    public bool GameSeesDown => Holding || PhysicallyHeld;

    public PushToTalkSend Press()
    {
        if (Holding)
            return PushToTalkSend.Nothing;

        Holding = true;
        return PhysicallyHeld ? PushToTalkSend.Nothing : PushToTalkSend.Down;
    }

    /// <summary>If the user is still holding it, the game keeps seeing it down until their own release.</summary>
    public PushToTalkSend Release()
    {
        if (!Holding)
            return PushToTalkSend.Nothing;

        Holding = false;
        return PhysicallyHeld ? PushToTalkSend.Nothing : PushToTalkSend.Up;
    }

    /// <summary>Returns true to hide the event from every other application.</summary>
    public bool HandleKey(ScanKey key, bool isDown, bool isInjected) =>
        Binding.Key == key && HandlePhysical(isDown, isInjected);

    /// <summary>Returns true to hide the event from every other application.</summary>
    public bool HandleMouse(PushToTalkMouseButton button, bool isDown, bool isInjected) =>
        Binding.MouseButton == button && HandlePhysical(isDown, isInjected);

    /// <summary>
    /// Adopts the system's view after events may have been missed. While holding, the system only shows our own
    /// press, so the user is assumed not to hold it: better an early release than a stuck one.
    /// </summary>
    public void SyncPhysical(bool isDown) => PhysicallyHeld = !Holding && isDown;

    /// <summary>
    /// Call before <see cref="Press"/>. No mouse hook runs while idle, so a mouse binding adopts the system's view;
    /// the keyboard hook can miss an up sent to a more-elevated window, so a key only adopts the system's "up".
    /// </summary>
    public void SyncBeforePress(bool systemSeesDown)
    {
        if (!Holding && (Binding.MouseButton is not null || !systemSeesDown))
            PhysicallyHeld = systemSeesDown;
    }

    /// <summary>
    /// Switches binding, moving any hold across: send the first result with the old binding and the second with
    /// the new one. <paramref name="newIsDown"/> is the system's view of the new binding.
    /// </summary>
    public (PushToTalkSend ReleaseOld, PushToTalkSend PressNew) SetBinding(PushToTalkBinding binding, bool newIsDown)
    {
        if (binding == Binding)
            return (PushToTalkSend.Nothing, PushToTalkSend.Nothing);

        var wasHolding = Holding;
        var releaseOld = Release();
        Binding = binding;
        SyncPhysical(newIsDown);
        return (releaseOld, wasHolding ? Press() : PushToTalkSend.Nothing);
    }

    // Injected events, our own included, never change what the user is physically doing.
    private bool HandlePhysical(bool isDown, bool isInjected)
    {
        if (isInjected)
            return false;

        PhysicallyHeld = isDown;
        return Holding;
    }
}
