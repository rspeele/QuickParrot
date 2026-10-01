using QuickParrot.Core.Replay;

namespace QuickParrot.Core.Tests.Replay;

public class ReplayCapturePolicyTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("a", "a")]
    public void Disabled_AlwaysDisables(string? target, string? running)
    {
        Assert.Equal(ReplayCaptureStep.Disable, ReplayCapturePolicy.Decide(false, target, running, null));
    }

    [Fact]
    public void RunningOnTheTarget_IsKept_EvenIfThatDeviceOnceFailed()
    {
        Assert.Equal(ReplayCaptureStep.Keep, ReplayCapturePolicy.Decide(true, "a", "a", "a"));
    }

    [Fact]
    public void RunningElsewhere_Restarts()
    {
        Assert.Equal(ReplayCaptureStep.Start, ReplayCapturePolicy.Decide(true, "b", "a", null));
    }

    [Fact]
    public void NothingRunning_Starts()
    {
        Assert.Equal(ReplayCaptureStep.Start, ReplayCapturePolicy.Decide(true, "a", null, "b"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a")]
    public void NoTarget_ReportsNoDevice(string? running)
    {
        Assert.Equal(ReplayCaptureStep.NoDevice, ReplayCapturePolicy.Decide(true, null, running, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("b")]
    public void FailedTarget_Waits(string? running)
    {
        Assert.Equal(ReplayCaptureStep.Wait, ReplayCapturePolicy.Decide(true, "a", running, "a"));
    }

    [Fact]
    public void DeviceIdsCompareExactly()
    {
        Assert.Equal(ReplayCaptureStep.Start, ReplayCapturePolicy.Decide(true, "A", "a", "a"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(ReplayCapturePolicy.HealthySessionMilliseconds - 1, true)]
    [InlineData(ReplayCapturePolicy.HealthySessionMilliseconds, false)]
    public void StoppedTooSoon_OnlyBeforeTheHealthyMark(long ranMilliseconds, bool tooSoon)
    {
        Assert.Equal(tooSoon, ReplayCapturePolicy.StoppedTooSoon(ranMilliseconds));
    }
}
