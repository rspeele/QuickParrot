using System.Collections.Immutable;

namespace QuickParrot.Core.Library;

public sealed class LibrarySearchResult
{
    internal LibrarySearchResult(string name, string phraseName, ImmutableArray<FolderEntry> clips)
    {
        Name = name;
        PhraseName = phraseName;
        Clips = clips;
    }

    public string Name { get; }
    public string PhraseName { get; }
    public ImmutableArray<FolderEntry> Clips { get; }
    public FolderEntry RepresentativeClip => Clips[0];

    public FolderEntry SelectClip(Func<int, int> randomIndex) =>
        Clips.Length == 1 ? Clips[0] : Clips[randomIndex(Clips.Length)];
}
