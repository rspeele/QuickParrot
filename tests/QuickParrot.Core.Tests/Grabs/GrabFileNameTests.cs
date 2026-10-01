using QuickParrot.Core.Grabs;

namespace QuickParrot.Core.Tests.Grabs;

public sealed class GrabFileNameTests
{
    private static readonly DateTimeOffset At = new DateTime(2026, 9, 30, 14, 25, 1, 500, DateTimeKind.Local);

    [Fact]
    public void Format_UsesLocalTimeToTheSecond()
    {
        Assert.Equal("grab-20260930-142501", GrabFileName.Format(At));
        Assert.Equal("grab-20260930-142501-3", GrabFileName.Format(At, 3));
    }

    [Fact]
    public void TryParse_RoundTrips()
    {
        Assert.True(GrabFileName.TryParse("grab-20260930-142501-3", out var grabbedAt, out var sequence));

        Assert.Equal(At.AddMilliseconds(-500), grabbedAt);
        Assert.Equal(3, sequence);
    }

    [Theory]
    [InlineData("grab-20260930")]
    [InlineData("grab-20261330-142501")]
    [InlineData("clip-20260930-142501")]
    [InlineData("grab-20260930-142501-")]
    [InlineData("grab-20260930-142501-1")]
    [InlineData("grab-20260930-142501-x")]
    [InlineData("grab-20260930-142501.wav")]
    public void TryParse_RejectsOtherNames(string id) => Assert.False(GrabFileName.TryParse(id, out _, out _));
}
