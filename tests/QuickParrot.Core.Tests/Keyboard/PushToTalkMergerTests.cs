using QuickParrot.Core.Keyboard;

namespace QuickParrot.Core.Tests.Keyboard;

public class PushToTalkMergerTests
{
    private static readonly ScanKey V = new(0x2F, false);
    private static readonly ScanKey C = new(0x2E, false);

    private readonly PushToTalkMerger _merger = new(PushToTalkBinding.FromKey(V));
    private readonly List<PushToTalkSend> _sent = [];

    // What the game sees: passed-through physical events plus whatever the merger told us to inject.
    private bool _gameDown;

    private void Press() => Apply(_merger.Press());

    private void Release() => Apply(_merger.Release());

    private bool Physical(bool down)
    {
        var swallowed = _merger.HandleKey(V, down, isInjected: false);
        if (!swallowed)
            _gameDown = down;

        AssertConsistent();
        return swallowed;
    }

    private void Apply(PushToTalkSend send)
    {
        if (send == PushToTalkSend.Nothing)
        {
            AssertConsistent();
            return;
        }

        _sent.Add(send);
        Assert.Equal(send == PushToTalkSend.Up, _gameDown); // never a redundant down or an unmatched up
        _gameDown = send == PushToTalkSend.Down;
        AssertConsistent();
    }

    private void AssertConsistent() => Assert.Equal(_merger.GameSeesDown, _gameDown);

    [Fact]
    public void PressAndRelease_InjectDownThenUp()
    {
        Press();
        Assert.True(_gameDown);
        Release();

        Assert.Equal([PushToTalkSend.Down, PushToTalkSend.Up], _sent);
        Assert.False(_gameDown);
    }

    [Fact]
    public void RepeatedPressOrRelease_SendsNothingMore()
    {
        Press();
        Press();
        Release();
        Release();

        Assert.Equal([PushToTalkSend.Down, PushToTalkSend.Up], _sent);
    }

    [Fact]
    public void ReleaseWithoutPress_SendsNothing()
    {
        Release();

        Assert.Empty(_sent);
    }

    [Fact]
    public void PhysicalEvents_PassThroughWhenNotHolding()
    {
        Assert.False(Physical(true));
        Assert.False(Physical(true)); // auto-repeat
        Assert.False(Physical(false));
        Assert.Empty(_sent);
    }

    [Fact]
    public void PhysicalDownBeforePress_PressSendsNothing_AndUsersUpPassesThrough()
    {
        Physical(true);
        Press();
        Release();

        Assert.Empty(_sent);
        Assert.True(_gameDown);
        Assert.False(Physical(false));
        Assert.False(_gameDown);
    }

    [Fact]
    public void PhysicalUpDuringHold_IsSwallowed_SoTheClipKeepsTransmitting()
    {
        Physical(true);
        Press();

        Assert.True(Physical(false));
        Assert.True(_gameDown);

        Release();
        Assert.Equal([PushToTalkSend.Up], _sent);
        Assert.False(_gameDown);
    }

    [Fact]
    public void PhysicalDownAndUpDuringHold_AreSwallowed()
    {
        Press();

        Assert.True(Physical(true));
        Assert.True(Physical(false));
        Assert.True(_gameDown);

        Release();
        Assert.Equal([PushToTalkSend.Down, PushToTalkSend.Up], _sent);
    }

    [Fact]
    public void PhysicalDownDuringHold_StillHeldAtRelease_LeavesTheUpToTheUser()
    {
        Press();
        Assert.True(Physical(true));

        Release();
        Assert.Equal([PushToTalkSend.Down], _sent);
        Assert.True(_gameDown);

        Assert.False(Physical(false));
        Assert.False(_gameDown);
    }

    [Fact]
    public void AutoRepeatDuringHold_IsSwallowed()
    {
        Physical(true);
        Press();

        Assert.True(Physical(true));
        Assert.True(Physical(true));

        Release();
        Assert.False(Physical(true)); // repeats pass through again once we let go
    }

    [Fact]
    public void InjectedEvents_PassThroughAndAreIgnored()
    {
        Press();

        Assert.False(_merger.HandleKey(V, isDown: false, isInjected: true));
        Assert.False(_merger.HandleKey(V, isDown: true, isInjected: true));
        Assert.False(_merger.PhysicallyHeld);

        Release();
        Assert.False(_merger.HandleKey(V, isDown: true, isInjected: true));
        Assert.False(_merger.PhysicallyHeld);
        Assert.Equal([PushToTalkSend.Down, PushToTalkSend.Up], _sent);
    }

    [Fact]
    public void OtherKeysAndMouseButtons_AreIgnored()
    {
        Press();

        Assert.False(_merger.HandleKey(C, isDown: true, isInjected: false));
        Assert.False(_merger.HandleKey(V with { IsExtended = true }, isDown: true, isInjected: false));
        Assert.False(_merger.HandleMouse(PushToTalkMouseButton.X1, isDown: true, isInjected: false));
        Assert.False(_merger.PhysicallyHeld);
    }

    [Fact]
    public void MouseBinding_MergesTheSameWay()
    {
        var merger = new PushToTalkMerger(PushToTalkBinding.FromMouse(PushToTalkMouseButton.X1));

        Assert.Equal(PushToTalkSend.Down, merger.Press());
        Assert.True(merger.HandleMouse(PushToTalkMouseButton.X1, isDown: true, isInjected: false));
        Assert.False(merger.HandleMouse(PushToTalkMouseButton.X2, isDown: true, isInjected: false));
        Assert.False(merger.HandleMouse(PushToTalkMouseButton.X1, isDown: false, isInjected: true));
        Assert.False(merger.HandleKey(V, isDown: true, isInjected: false));
        Assert.Equal(PushToTalkSend.Nothing, merger.Release());
        Assert.False(merger.HandleMouse(PushToTalkMouseButton.X1, isDown: false, isInjected: false));
    }

    [Fact]
    public void ReleaseForShutdownWhileHolding_SendsUp()
    {
        Physical(true);
        Press();
        Physical(false);

        Release();

        Assert.False(_gameDown);
        Assert.False(_merger.Holding);
    }

    [Fact]
    public void SyncWhileIdle_AdoptsTheSystemsView()
    {
        _merger.SyncPhysical(true);
        Assert.True(_merger.PhysicallyHeld);
        Assert.Equal(PushToTalkSend.Nothing, _merger.Press());

        _merger.SyncPhysical(true); // while holding, the system's view is our own press
        Assert.False(_merger.PhysicallyHeld);
        Assert.Equal(PushToTalkSend.Up, _merger.Release());

        _merger.SyncPhysical(false);
        Assert.Equal(PushToTalkSend.Down, _merger.Press());
    }

    [Fact]
    public void SyncBeforePress_KeyUpMissedByTheHook_StillPresses()
    {
        Physical(true); // its up then goes to a more-elevated window, unseen

        _merger.SyncBeforePress(systemSeesDown: false);

        Assert.Equal(PushToTalkSend.Down, _merger.Press());
    }

    [Fact]
    public void SyncBeforePress_KeyTrustsTheHookOverAStaleSystemDown()
    {
        _merger.SyncBeforePress(systemSeesDown: true); // e.g. our own up not yet seen by the system

        Assert.Equal(PushToTalkSend.Down, _merger.Press());
    }

    [Fact]
    public void SyncBeforePress_MouseAdoptsTheSystemsView_ButNotWhileHolding()
    {
        var merger = new PushToTalkMerger(PushToTalkBinding.FromMouse(PushToTalkMouseButton.X1));

        merger.SyncBeforePress(systemSeesDown: true);
        Assert.Equal(PushToTalkSend.Nothing, merger.Press());

        merger.HandleMouse(PushToTalkMouseButton.X1, isDown: false, isInjected: false);
        merger.SyncBeforePress(systemSeesDown: true);
        Assert.False(merger.PhysicallyHeld);
        Assert.Equal(PushToTalkSend.Up, merger.Release());
    }

    [Fact]
    public void SyncWhileHolding_AfterAMissedUp_StillReleases()
    {
        Press();
        Physical(true); // the user's up is then lost, e.g. on the secure desktop

        _merger.SyncPhysical(true);

        Assert.Equal(PushToTalkSend.Up, _merger.Release());
    }

    [Fact]
    public void SetBindingWhileIdle_SendsNothing_AndTracksTheNewBinding()
    {
        var mouse = PushToTalkBinding.FromMouse(PushToTalkMouseButton.Middle);

        Assert.Equal((PushToTalkSend.Nothing, PushToTalkSend.Nothing), _merger.SetBinding(mouse, newIsDown: true));
        Assert.Equal(mouse, _merger.Binding);
        Assert.True(_merger.PhysicallyHeld);
        Assert.False(_merger.HandleKey(V, isDown: false, isInjected: false));
        Assert.True(_merger.PhysicallyHeld);
    }

    [Fact]
    public void SetBindingWhileHolding_MovesTheHold()
    {
        _merger.Press();

        var result = _merger.SetBinding(PushToTalkBinding.FromKey(C), newIsDown: false);

        Assert.Equal((PushToTalkSend.Up, PushToTalkSend.Down), result);
        Assert.True(_merger.Holding);
        Assert.True(_merger.HandleKey(C, isDown: true, isInjected: false));
        Assert.False(_merger.HandleKey(V, isDown: true, isInjected: false));
    }

    [Fact]
    public void SetBindingWhileBothHeld_LeavesTheOldKeyToTheUser_AndSkipsAHeldNewKey()
    {
        _merger.Press();
        _merger.HandleKey(V, isDown: true, isInjected: false);

        var result = _merger.SetBinding(PushToTalkBinding.FromKey(C), newIsDown: true);

        Assert.Equal((PushToTalkSend.Nothing, PushToTalkSend.Nothing), result);
        Assert.True(_merger.Holding);
        Assert.True(_merger.PhysicallyHeld);
    }

    [Fact]
    public void SetBindingToTheSameBinding_ChangesNothing()
    {
        _merger.Press();
        _merger.HandleKey(V, isDown: true, isInjected: false);

        Assert.Equal((PushToTalkSend.Nothing, PushToTalkSend.Nothing), _merger.SetBinding(_merger.Binding, false));
        Assert.True(_merger.PhysicallyHeld);
    }

    private enum Step
    {
        PhysicalDown,
        PhysicalUp,
        Press,
        Release,
    }

    // Every interleaving of 7 steps: the game sees down exactly while either side holds it, with no redundant
    // or unmatched injections (checked in Apply).
    [Fact]
    public void AllInterleavings_KeepTheGameConsistent()
    {
        const int length = 7;
        var steps = Enum.GetValues<Step>();
        var sequences = (int)Math.Pow(steps.Length, length);
        for (var n = 0; n < sequences; n++)
        {
            var harness = new PushToTalkMergerTests();
            var physical = false;
            var holding = false;
            var code = n;
            for (var i = 0; i < length; i++, code /= steps.Length)
            {
                switch (steps[code % steps.Length])
                {
                    case Step.PhysicalDown:
                        harness.Physical(physical = true);
                        break;
                    case Step.PhysicalUp when physical:
                        harness.Physical(physical = false);
                        break;
                    case Step.Press:
                        harness.Press();
                        holding = true;
                        break;
                    case Step.Release:
                        harness.Release();
                        holding = false;
                        break;
                }

                Assert.Equal(holding || physical, harness._gameDown);
            }
        }
    }
}
