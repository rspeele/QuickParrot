using System.Collections.Immutable;
using System.Drawing;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

public sealed class SearchGeometryTests
{
    [Theory]
    [InlineData(SmallFolderLayout.List)]
    [InlineData(SmallFolderLayout.Ring)]
    public void SearchAlwaysShowsQueryAboveANumberedList(SmallFolderLayout preference)
    {
        var state = ViewStates.Wheel(9, "") with { SearchQuery = "number one", SmallFolderLayout = preference };
        var layout = OverlayLayoutGeometry.Compute(state, 1);
        Assert.Equal("Search library", layout.Title.Text);
        Assert.Equal("number one▏", layout.Subtitle!.Text);
        Assert.Contains("Enter plays 1", layout.Note!.Text);
        Assert.Equal(9, layout.Items.Count);
        Assert.True(layout.Items[0].Bounds.Top > layout.Panels[0].Bounds.Bottom);
        Assert.True(layout.Items[^1].Bounds.Bottom < layout.CanvasSize.Height);
        Assert.True(layout.Items.Zip(layout.Items.Skip(1)).All(pair => pair.First.Bounds.Bottom < pair.Second.Bounds.Top));
    }

    [Fact]
    public void EmptySearchHasPromptAndNoMatchesHasCancelHint()
    {
        var state = ViewStates.Wheel(0, "") with { SearchQuery = "" };
        var layout = OverlayLayoutGeometry.Compute(state, 1);
        Assert.Equal("Type a clip or folder name…", layout.Subtitle!.Text);
        Assert.Equal("No matches · Esc cancels", layout.Note!.Text);
    }

    [Theory]
    [InlineData(800, 600, 192)]
    [InlineData(1920, 1080, 96)]
    [InlineData(3840, 2160, 192)]
    public void SearchFitsMonitor(int width, int height, int dpi)
    {
        var state = ViewStates.Wheel(9, "") with { SearchQuery = "query" };
        var layout = OverlayLayoutGeometry.ComputeForMonitor(state, new Size(width, height), dpi);
        Assert.True(layout.CanvasSize.Width < width);
        Assert.True(layout.CanvasSize.Height < height);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(1.37f)]
    [InlineData(2f)]
    public void FilenameAndFoldersUseFixedNonOverlappingColumns(float scale)
    {
        var entries = ViewStates.Wheel(9).WheelEntries.Select(entry => entry with
        {
            IsFolder = false,
            Name = "Clip.wav",
            FolderContext = (entry.Number % 3) switch
            {
                0 => null,
                1 => new LibrarySearchFolderContext("Top", "Top"),
                _ => new LibrarySearchFolderContext(new string('A', 300), "Parent"),
            },
        }).ToImmutableArray();
        var state = ViewStates.Wheel(9) with { SearchQuery = "parent", WheelEntries = entries };
        var layout = OverlayLayoutGeometry.Compute(state, scale);
        foreach (var item in layout.Items)
        {
            var top = Assert.IsType<OverlayLabel>(item.TopFolder);
            var parent = Assert.IsType<OverlayLabel>(item.ParentFolder);
            Assert.Equal("Clip", item.Name);
            Assert.Equal(OverlayFont.Semibold, top.Font);
            Assert.Equal(14 * scale, top.FontPx);
            Assert.Equal(OverlayFont.Regular, parent.Font);
            Assert.Equal(12 * scale, parent.FontPx);
            Assert.True(top.FontPx < item.NameFontPx);
            Assert.True(top.FontPx > parent.FontPx);
            Assert.True(item.NameBounds.Width > 2 * 335 * scale);
            Assert.True(top.Bounds.Width < item.NameBounds.Width / 5);
            Assert.True(parent.Bounds.Width < item.NameBounds.Width / 5);
            Assert.True(item.Bounds.Contains(item.NameBounds));
            Assert.True(item.Bounds.Contains(top.Bounds));
            Assert.True(item.Bounds.Contains(parent.Bounds));
            Assert.True(item.Bounds.Contains(item.NumberBounds));
            Assert.True(item.NumberBounds.Right < item.NameBounds.Left);
            Assert.True(item.NameBounds.Right < top.Bounds.Left);
            Assert.True(top.Bounds.Right < parent.Bounds.Left);
            Assert.Equal(item.Bounds.Y, item.NameBounds.Y);
            Assert.Equal(item.Bounds.Y, top.Bounds.Y);
            Assert.Equal(item.Bounds.Y, parent.Bounds.Y);
            Assert.Equal(layout.Items[0].NameBounds.X, item.NameBounds.X);
            Assert.Equal(layout.Items[0].NameBounds.Width, item.NameBounds.Width);
            Assert.Equal(layout.Items[0].TopFolder!.Bounds.X, top.Bounds.X);
            Assert.Equal(layout.Items[0].TopFolder!.Bounds.Width, top.Bounds.Width);
            Assert.Equal(layout.Items[0].ParentFolder!.Bounds.X, parent.Bounds.X);
            Assert.Equal(layout.Items[0].ParentFolder!.Bounds.Width, parent.Bounds.Width);
            Assert.Equal(layout.ColumnLabels[0].Bounds.X, item.NameBounds.X, 3);
            Assert.Equal(layout.ColumnLabels[1].Bounds.X, top.Bounds.X, 3);
            Assert.Equal(layout.ColumnLabels[2].Bounds.X, parent.Bounds.X, 3);
        }
        Assert.Equal(new[] { "Clip", "Top folder", "Parent folder" }, layout.ColumnLabels.Select(label => label.Text));
        Assert.Equal("", layout.Items[2].TopFolder!.Text);
        Assert.Equal("", layout.Items[2].ParentFolder!.Text);
    }

    [Fact]
    public void TopFolderPaletteIsDistinctAndRootHasNoColor()
    {
        var entries = Enumerable.Range(0, LibrarySearch.FolderColorCount)
            .Select(index => new NumberedEntry(index + 1, "Clip.wav", false)
            {
                FolderContext = new LibrarySearchFolderContext("Top", "Parent") { ColorIndex = index },
            }).Append(new NumberedEntry(9, "Root.wav", false)).ToImmutableArray();
        var state = ViewStates.Wheel(9) with { SearchQuery = "", WheelEntries = entries };
        var layout = OverlayLayoutGeometry.Compute(state, 1);
        Assert.Equal(LibrarySearch.FolderColorCount, layout.Items.Take(LibrarySearch.FolderColorCount)
            .Select(item => item.TopFolderColor).OfType<Color>().Distinct().Count());
        Assert.Null(layout.Items[^1].TopFolderColor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(8)]
    public void MissingOrUnknownFolderColorFallsBackToMutedText(int? colorIndex)
    {
        var entry = new NumberedEntry(1, "Clip.wav", false)
        {
            FolderContext = new LibrarySearchFolderContext("Top", "Parent") { ColorIndex = colorIndex },
        };
        var state = ViewStates.Wheel(1) with { SearchQuery = "", WheelEntries = [entry] };
        Assert.Null(Assert.Single(OverlayLayoutGeometry.Compute(state, 1).Items).TopFolderColor);
    }

    [Fact]
    public void RootSearchNameIsCenteredAndOrdinaryNavigationIgnoresContext()
    {
        var state = ViewStates.Wheel(1);
        var searchItem = Assert.Single(OverlayLayoutGeometry.Compute(state with { SearchQuery = "" }, 1).Items);
        Assert.Equal("", searchItem.TopFolder!.Text);
        Assert.Equal("", searchItem.ParentFolder!.Text);
        Assert.Equal(searchItem.Bounds.Y, searchItem.NameBounds.Y);
        Assert.Equal(searchItem.Bounds.Height, searchItem.NameBounds.Height);

        var entry = state.WheelEntries[0] with { FolderContext = new LibrarySearchFolderContext("Top", "Parent") };
        var item = Assert.Single(OverlayLayoutGeometry.Compute(state with { WheelEntries = [entry] }, 1).Items);
        Assert.Null(item.TopFolder);
        Assert.Null(item.ParentFolder);
    }
}
