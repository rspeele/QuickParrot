using QuickParrot.Core.Navigation;

namespace QuickParrot.Core.Tests;

public class OverlayCapacityTests
{
    [Fact]
    public void TruncationWarning_SetWhenMoreThan81Entries() =>
        Assert.Equal(
            "This folder has 85 entries; the overlay only shows the first 81. Consider subfolders.",
            OverlayCapacity.TruncationWarning(85));

    [Theory]
    [InlineData(0)]
    [InlineData(81)]
    public void TruncationWarning_NullAt81OrFewerEntries(int count) =>
        Assert.Null(OverlayCapacity.TruncationWarning(count));
}
