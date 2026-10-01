using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorGridTests
{
    private static FakeFolderSource CreateFilledSource(int fileCount, out List<string> paths)
    {
        var source = new FakeFolderSource();
        paths = [];
        for (var i = 0; i < fileCount; i++)
            paths.Add(source.AddFile("", $"clip{i:D3}.wav"));
        return source;
    }

    [Fact]
    public void TenEntries_IsGrid()
    {
        var source = CreateFilledSource(10, out _);
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());

        Assert.Equal(OverlayLayoutKind.Grid, nav.ViewState!.Layout);
    }

    [Fact]
    public void NineEntries_IsWheel()
    {
        var source = CreateFilledSource(9, out _);
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());

        Assert.Equal(OverlayLayoutKind.Wheel, nav.ViewState!.Layout);
    }

    [Fact]
    public void ColumnsFillInOrder_LastColumnPartial()
    {
        var source = CreateFilledSource(19, out var paths); // columns of 9, 9, 1
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        var view = nav.ViewState!;

        Assert.Equal(3, view.GridColumns.Count);
        Assert.Equal(9, view.GridColumns[0].Entries.Count);
        Assert.Equal(9, view.GridColumns[1].Entries.Count);
        Assert.Single(view.GridColumns[2].Entries);
        Assert.Equal(paths[0], view.GridColumns[0].Entries[0].RelativePath);
        Assert.Equal(paths[9], view.GridColumns[1].Entries[0].RelativePath);
        Assert.Equal(paths[18], view.GridColumns[2].Entries[0].RelativePath);
        Assert.False(view.Truncated);
    }

    [Fact]
    public void MoreThan81Entries_TruncatesAndSetsFlag()
    {
        var source = CreateFilledSource(100, out _);
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        var view = nav.ViewState!;

        Assert.Equal(81, view.GridColumns.Sum(c => c.Entries.Count));
        Assert.Equal(9, view.GridColumns.Count);
        Assert.True(view.Truncated);
    }

    [Fact]
    public void DigitZoomsIntoColumn()
    {
        var source = CreateFilledSource(15, out _);
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(2, false));

        Assert.Equal(2, nav.ViewState!.ZoomedColumn);
    }

    [Fact]
    public void ZoomedDigitSelectsWithinColumn()
    {
        var source = CreateFilledSource(15, out var paths); // column 1: idx 0-8, column 2: idx 9-14
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(2, false));
        var actions = nav.Handle(new DigitPressed(3, false)); // 3rd entry of column 2 -> absolute index 11

        var playClip = Assert.IsType<PlayClip>(Assert.Single(actions));
        Assert.Equal(paths[11], playClip.RelativePath);
    }

    [Fact]
    public void ZeroUnzoomsColumn_WithoutGoingUp()
    {
        var source = CreateFilledSource(15, out _);
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        Assert.NotNull(nav.ViewState!.ZoomedColumn);

        nav.Handle(new DigitPressed(0, false));

        Assert.Null(nav.ViewState!.ZoomedColumn);
        Assert.Equal(OverlayLayoutKind.Grid, nav.ViewState!.Layout);
        Assert.Equal("", nav.ViewState!.FolderPath);
    }

    [Fact]
    public void ColumnOutOfRange_DoesNothing()
    {
        var source = CreateFilledSource(10, out _); // only 2 columns
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(9, false));

        Assert.Null(nav.ViewState!.ZoomedColumn);
    }

    [Fact]
    public void SelectingFolderFromWithinColumn_ResetsZoom()
    {
        var source = new FakeFolderSource();
        for (var i = 0; i < 9; i++)
            source.AddFile("", $"clip{i:D3}.wav");
        var folder = source.AddFolder("", "Sub"); // folders sort first, so "Sub" lands at index 0

        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false)); // zoom column 1, which starts with "Sub"
        nav.Handle(new DigitPressed(1, false)); // select "Sub" within the column

        Assert.Equal(folder, nav.ViewState!.FolderPath);
        Assert.Null(nav.ViewState!.ZoomedColumn);
    }

    [Fact]
    public void ZoomedColumnVanishingOnDisk_DoesNotThrow()
    {
        var source = CreateFilledSource(20, out _); // columns of 9, 9, 2
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(3, false));
        source.RemoveFile("", "clip018.wav");
        source.RemoveFile("", "clip019.wav");
        nav.Handle(new DigitPressed(5, false)); // no-op, but refreshes and drops the stale zoom
        Assert.Null(nav.ViewState!.ZoomedColumn);

        var actions = nav.Handle(new DigitPressed(1, false));

        Assert.Empty(actions);
        Assert.Equal(1, nav.ViewState!.ZoomedColumn);
    }

    [Fact]
    public void ZoomedDigitBeyondPartialColumn_DoesNothing()
    {
        var source = CreateFilledSource(11, out _); // columns of 9, 2
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(2, false));
        var actions = nav.Handle(new DigitPressed(3, false));

        Assert.Empty(actions);
        Assert.Equal(2, nav.ViewState!.ZoomedColumn);
    }
}
