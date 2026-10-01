using QuickParrot.Core.Grabs;

namespace QuickParrot.Core.Tests.Grabs;

public class PendingGrabCleanupRuleTests
{
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 2, true)]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, true)]
    public void ShouldDelete_MatchesTheRule(bool discarded, int savedClipCount, bool expected) =>
        Assert.Equal(expected, PendingGrabCleanupRule.ShouldDelete(discarded, savedClipCount));
}
