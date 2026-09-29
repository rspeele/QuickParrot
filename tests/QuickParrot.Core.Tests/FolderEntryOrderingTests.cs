using QuickParrot.Core.Library;

namespace QuickParrot.Core.Tests;

public class FolderEntryOrderingTests
{
    [Fact]
    public void FoldersSortBeforeFiles_ThenByNameCaseInsensitive()
    {
        var entries = new List<FolderEntry>
        {
            new("banana.wav", false, "banana.wav"),
            new("Zebra", true, "Zebra"),
            new("apple.wav", false, "apple.wav"),
            new("albert", true, "albert"),
        };

        entries.Sort(FolderEntryOrdering.Comparer);

        Assert.Equal(["albert", "Zebra", "apple.wav", "banana.wav"], entries.Select(e => e.Name));
    }
}
