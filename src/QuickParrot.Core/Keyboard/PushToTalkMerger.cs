namespace QuickParrot.Core.Keyboard;

/// <summary>What to inject for the push-to-talk binding.</summary>
public enum PushToTalkSend
{
    Nothing,
    Down,
    Up,
}

/// <summary>
/// Makes the game see one push-to-talk input that's down while the user physically holds it or QuickParrot holds
/// it. Decides what to inject and which physical events to hide. Not thread-safe; handling events never allocates.
/// </summary>
public sealed class PushToTalkMerger
{
    private readonly List<PushToTalkBinding> _pendingUps = new(4);

    public PushToTalkMerger(PushToTalkBinding binding)
    {
        Binding = binding;
    }

    public PushToTalkBinding Binding { get; private set; }

    /// <summary>QuickParrot is holding the binding for a clip.</summary>
    public bool Holding { get; private set; }

    /// <summary>The user is physically holding the binding, as far as is known.</summary>
    internal bool PhysicallyHeld { get; private set; }

    /// <summary>Whether the game should currently see the binding as down.</summary>
    internal bool GameSeesDown => Holding || PhysicallyHeld;

    /// <summary>Bindings whose injected up failed, so the game may still see them down.</summary>
    public int PendingUpCount => _pendingUps.Count;

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
    public bool HandleKey(ScanKey key, bool isDown, bool isInjected)
    {
        // The user's own press or release reaches the game, so a failed up for it no longer matters.
        if (!isInjected && _pendingUps.Count != 0)
            _pendingUps.Remove(PushToTalkBinding.FromKey(key));

        return Binding.Key == key && HandlePhysical(isDown, isInjected);
    }

    /// <summary>Returns true to hide the event from every other application.</summary>
    public bool HandleMouse(PushToTalkMouseButton button, bool isDown, bool isInjected)
    {
        if (!isInjected && _pendingUps.Count != 0)
            _pendingUps.Remove(PushToTalkBinding.FromMouse(button));

        return Binding.MouseButton == button && HandlePhysical(isDown, isInjected);
    }

    /// <summary>Call after injecting an up for <paramref name="binding"/>; a failed one is kept for retrying.</summary>
    public void UpSent(PushToTalkBinding binding, bool succeeded)
    {
        if (succeeded)
            _pendingUps.Remove(binding);
        else if (!_pendingUps.Contains(binding))
            _pendingUps.Add(binding);
    }

    /// <summary>
    /// Takes the oldest failed up worth retrying; report the retry with <see cref="UpSent"/>. One for a binding
    /// QuickParrot holds again is dropped, since releasing that hold sends a fresh up.
    /// </summary>
    public bool TryTakePendingUp(out PushToTalkBinding binding)
    {
        while (_pendingUps.Count != 0)
        {
            binding = _pendingUps[0];
            _pendingUps.RemoveAt(0);
            if (!Holding || binding != Binding)
                return true;
        }

        binding = default;
        return false;
    }

    /// <summary>
    /// Everything the game may see down because of QuickParrot, for an emergency release. Includes a hold the user
    /// also has, since their release is hidden while holding: better cutting them off than leaving it stuck.
    /// </summary>
    public PushToTalkBinding[] HeldByUs()
    {
        var held = new List<PushToTalkBinding>(_pendingUps);
        if (Holding && !held.Contains(Binding))
            held.Add(Binding);

        return [.. held];
    }

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
