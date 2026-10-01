using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests.Library;

public class LibraryChangeFilterTests
{
    [Theory]
    [InlineData(".3fa2b1c4d5e6f7a8b9c0d1e2f3a4b5c6.tmp")]
    [InlineData(".3FA2B1C4D5E6F7A8B9C0D1E2F3A4B5C6.TMP")]
    public void ClipEncoderTempFiles_AreIgnorable(string fileName)
    {
        Assert.True(LibraryChangeFilter.IsIgnorableTempFile(fileName));
    }

    [Theory]
    [InlineData("clip.wav")]
    [InlineData("clip.mp3")]
    [InlineData(".tmp")]
    [InlineData(".3fa2b1c4d5e6f7a8b9c0d1e2f3a4b5c6.wav")] // right shape, wrong extension
    [InlineData(".3fa2b1c4d5e6f7a8.tmp")] // too short to be the GUID
    [InlineData("Movies")]
    [InlineData(null)]
    public void OrdinaryEntries_AreNotIgnorable(string? fileName)
    {
        Assert.False(LibraryChangeFilter.IsIgnorableTempFile(fileName));
    }
}
