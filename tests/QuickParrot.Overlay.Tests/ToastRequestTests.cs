namespace QuickParrot.Overlay.Tests;

public sealed class ToastRequestTests
{
    [Fact]
    public void IsLive_UntilTheDurationHasPassed()
    {
        var toast = ToastRequest.Create("Saved", false, TimeSpan.FromSeconds(1.5), now: 10_000);

        Assert.True(toast.IsLive(10_000));
        Assert.True(toast.IsLive(11_499));
        Assert.False(toast.IsLive(11_500));
    }

    [Fact]
    public void ChooseScene_ChordViewWins_ThenALiveToast_ThenNothing()
    {
        var state = ViewStates.Wheel(3);
        var toast = ToastRequest.Create("Saved", false, TimeSpan.FromSeconds(1), now: 0);

        Assert.Same(state, ToastRequest.ChooseScene(state, toast, 500));
        Assert.Same(toast, ToastRequest.ChooseScene(null, toast, 500));
        Assert.Null(ToastRequest.ChooseScene(null, toast, 1000));
        Assert.Null(ToastRequest.ChooseScene(null, null, 0));
    }
}
