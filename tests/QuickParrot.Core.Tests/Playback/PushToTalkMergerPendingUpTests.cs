using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Playback;

namespace QuickParrot.Core.Tests.Playback;

public class PushToTalkMergerPendingUpTests
{
    private static readonly ScanKey V = new(0x2F, false);
    private static readonly ScanKey C = new(0x2E, false);
    private static readonly PushToTalkBinding BindingV = PushToTalkBinding.FromKey(V);
    private static readonly PushToTalkBinding BindingC = PushToTalkBinding.FromKey(C);

    private readonly PushToTalkMerger _merger = new(BindingV);

    private void PressThenFailRelease()
    {
        Assert.Equal(PushToTalkSend.Down, _merger.Press());
        Assert.Equal(PushToTalkSend.Up, _merger.Release());
        _merger.UpSent(BindingV, succeeded: false);
    }

    [Fact]
    public void FailedUp_IsRetriedUntilItSucceeds()
    {
        PressThenFailRelease();

        Assert.True(_merger.TryTakePendingUp(out var binding));
        Assert.Equal(BindingV, binding);
        _merger.UpSent(binding, succeeded: false);
        Assert.Equal(1, _merger.PendingUpCount);

        Assert.True(_merger.TryTakePendingUp(out binding));
        _merger.UpSent(binding, succeeded: true);
        Assert.Equal(0, _merger.PendingUpCount);
        Assert.False(_merger.TryTakePendingUp(out _));
    }

    [Fact]
    public void RepeatedFailures_AreOnePendingUp()
    {
        PressThenFailRelease();
        _merger.UpSent(BindingV, succeeded: false);

        Assert.Equal(1, _merger.PendingUpCount);
    }

    [Fact]
    public void SucceededUp_ClearsAPendingOne()
    {
        PressThenFailRelease();

        _merger.Press();
        _merger.Release();
        _merger.UpSent(BindingV, succeeded: true);

        Assert.Equal(0, _merger.PendingUpCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UsersOwnPressOrRelease_MakesItMoot(bool isDown)
    {
        PressThenFailRelease();

        Assert.False(_merger.HandleKey(V, isDown, isInjected: false));

        Assert.Equal(0, _merger.PendingUpCount);
    }

    [Fact]
    public void InjectedEventsAndOtherKeys_LeaveItPending()
    {
        PressThenFailRelease();

        _merger.HandleKey(V, isDown: false, isInjected: true);
        _merger.HandleKey(C, isDown: false, isInjected: false);

        Assert.Equal(1, _merger.PendingUpCount);
    }

    [Fact]
    public void HoldingTheSameBindingAgain_DropsIt_SinceTheNextReleaseSendsAFreshUp()
    {
        PressThenFailRelease();

        Assert.Equal(PushToTalkSend.Down, _merger.Press());

        Assert.False(_merger.TryTakePendingUp(out _));
        Assert.Equal(0, _merger.PendingUpCount);
        Assert.Equal(PushToTalkSend.Up, _merger.Release());
    }

    [Fact]
    public void OldBindingsFailedUp_SurvivesAHoldOnTheNewBinding()
    {
        _merger.Press();
        var (releaseOld, pressNew) = _merger.SetBinding(BindingC, newIsDown: false);
        Assert.Equal((PushToTalkSend.Up, PushToTalkSend.Down), (releaseOld, pressNew));
        _merger.UpSent(BindingV, succeeded: false);

        Assert.True(_merger.TryTakePendingUp(out var binding));
        Assert.Equal(BindingV, binding);
    }

    [Fact]
    public void UsersOwnEventOnTheOldBinding_ClearsItsFailedUp_AndPassesThrough()
    {
        PressThenFailRelease();
        _merger.SetBinding(BindingC, newIsDown: false);

        Assert.False(_merger.HandleKey(V, isDown: false, isInjected: false));

        Assert.Equal(0, _merger.PendingUpCount);
    }

    [Fact]
    public void MouseButtonEvent_ClearsItsFailedUp()
    {
        var middle = PushToTalkBinding.FromMouse(PushToTalkMouseButton.Middle);
        var merger = new PushToTalkMerger(middle);
        merger.UpSent(middle, succeeded: false);

        merger.HandleMouse(PushToTalkMouseButton.X1, isDown: true, isInjected: false);
        Assert.Equal(1, merger.PendingUpCount);

        merger.HandleMouse(PushToTalkMouseButton.Middle, isDown: true, isInjected: false);
        Assert.Equal(0, merger.PendingUpCount);
    }

    [Fact]
    public void RetryingEveryPendingUpOnce_EndsWithFailuresStillPending()
    {
        _merger.UpSent(BindingV, succeeded: false);
        _merger.UpSent(BindingC, succeeded: false);

        var remaining = _merger.PendingUpCount;
        var tried = new List<PushToTalkBinding>();
        while (remaining-- > 0 && _merger.TryTakePendingUp(out var binding))
        {
            tried.Add(binding);
            _merger.UpSent(binding, succeeded: binding == BindingC);
        }

        Assert.Equal([BindingV, BindingC], tried);
        Assert.True(_merger.TryTakePendingUp(out var left));
        Assert.Equal(BindingV, left);
    }

    [Fact]
    public void HeldByUs_IsOurHoldPlusFailedUps()
    {
        Assert.Empty(_merger.HeldByUs());

        _merger.Press();
        Assert.Equal([BindingV], _merger.HeldByUs());

        _merger.SetBinding(BindingC, newIsDown: false);
        _merger.UpSent(BindingV, succeeded: false);
        Assert.Equal([BindingV, BindingC], _merger.HeldByUs());

        _merger.Release();
        _merger.UpSent(BindingC, succeeded: true);
        Assert.Equal([BindingV], _merger.HeldByUs());
    }

    [Fact]
    public void HeldByUs_KeepsOurHoldWhileTheUserHoldsIt_SinceTheirHiddenReleaseWouldLeaveItStuck()
    {
        _merger.HandleKey(V, isDown: true, isInjected: false);
        _merger.Press();

        Assert.Equal([BindingV], _merger.HeldByUs());
    }
}
