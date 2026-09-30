using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

public sealed class ListGeometryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void Items_AreNumberedTopToBottomInEntryOrder(int count)
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(count), 1);

        Assert.Equal(Enumerable.Range(1, count), layout.Items.Select(i => i.Number));
        Assert.Equal(Enumerable.Range(1, count).Select(n => $"Entry {n}"), layout.Items.Select(i => i.Name));
        Assert.Equal(layout.Items.Select(i => i.Bounds.Top).Order(), layout.Items.Select(i => i.Bounds.Top));
    }

    [Fact]
    public void FirstItem_IsAtTheTop()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(5), 1);

        Assert.True(layout.Items.Skip(1).All(i => i.Bounds.Top > layout.Items[0].Bounds.Top));
    }

    [Theory]
    [InlineData(1, 1f)]
    [InlineData(2, 1f)]
    [InlineData(5, 1f)]
    [InlineData(8, 1f)]
    [InlineData(9, 1f)]
    [InlineData(9, 1.37f)]
    public void Pills_DontOverlapEachOtherOrTheHeader_AndFitTheCanvas(int count, float scale)
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(count, "Movies"), scale);
        var header = layout.Panels.Single(p => p.Style == OverlayPanelStyle.Panel).Bounds;
        var canvas = new RectangleF(PointF.Empty, layout.CanvasSize);

        for (var i = 0; i < layout.Items.Count; i++)
        {
            var pill = layout.Items[i].Bounds;
            Assert.True(canvas.Contains(pill), $"pill {i + 1} is outside the canvas");
            Assert.False(pill.IntersectsWith(header), $"pill {i + 1} overlaps the header");
            for (var j = i + 1; j < layout.Items.Count; j++)
                Assert.False(pill.IntersectsWith(layout.Items[j].Bounds), $"pills {i + 1} and {j + 1} overlap");
        }
    }

    [Fact]
    public void FoldersGetAnIcon_AndEveryPartSitsInsideThePill()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(2), 1);
        var (folder, file) = (layout.Items[0], layout.Items[1]);

        Assert.True(folder.IsFolder);
        Assert.False(folder.IconBounds.IsEmpty);
        Assert.True(file.IconBounds.IsEmpty);
        foreach (var item in layout.Items)
        {
            Assert.True(item.Bounds.Contains(item.NumberBounds));
            Assert.True(item.Bounds.Contains(item.NameBounds));
            Assert.True(item.NameBounds.Left >= item.NumberBounds.Right);
            Assert.True(item.IconBounds.IsEmpty || item.NameBounds.Left >= item.IconBounds.Right);
        }
    }

    [Fact]
    public void FileNames_DropTheirExtension_FolderNamesDont()
    {
        var state = new OverlayViewState(
            "",
            OverlayLayoutKind.Wheel,
            [new(1, "Mr. T", true, "Mr. T"), new(2, "I pity the fool.mp3", false, "x"), new(3, ".wav", false, "y")],
            [],
            null,
            false);

        var names = OverlayLayoutGeometry.Compute(state, 1).Items.Select(i => i.Name);

        Assert.Equal(["Mr. T", "I pity the fool", ".wav"], names);
    }

    [Fact]
    public void Root_ShowsAppNameAndNoHint()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(3), 1);

        Assert.Equal(OverlayLayoutGeometry.RootTitle, layout.Title.Text);
        Assert.Null(layout.Hint);
        Assert.Null(layout.Subtitle);
    }

    [Fact]
    public void NestedFolder_ShowsItsOwnNameAndUpHint()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(3, "Movies/The Office"), 1);

        Assert.Equal("The Office", layout.Title.Text);
        Assert.Equal(OverlayLayoutGeometry.UpHint, layout.Hint?.Text);
        Assert.True(layout.Hint!.Bounds.Top >= layout.Title.Bounds.Bottom);
    }

    [Fact]
    public void EmptyFolder_SaysSoInTheHeader_AndDrawsNoItems()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(0, "Empty"), 1);

        Assert.Empty(layout.Items);
        Assert.Equal(OverlayLayoutGeometry.EmptyLabel, layout.Subtitle?.Text);
        Assert.Equal(OverlayLayoutGeometry.UpHint, layout.Hint?.Text);
    }

    [Fact]
    public void Scale_ScalesEverything()
    {
        var one = OverlayLayoutGeometry.Compute(ViewStates.Wheel(9, "Movies"), 1);
        var two = OverlayLayoutGeometry.Compute(ViewStates.Wheel(9, "Movies"), 2);

        Assert.InRange(two.CanvasSize.Width, 2 * one.CanvasSize.Width - 2, 2 * one.CanvasSize.Width + 2);
        Assert.InRange(two.CanvasSize.Height, 2 * one.CanvasSize.Height - 2, 2 * one.CanvasSize.Height + 2);
        Assert.Equal(2 * one.Items[3].Bounds.Width, two.Items[3].Bounds.Width, 0.01f);
        Assert.Equal(2 * one.Items[3].NameFontPx, two.Items[3].NameFontPx, 0.01f);
        Assert.Equal(2 * one.Title.FontPx, two.Title.FontPx, 0.01f);
    }
}
