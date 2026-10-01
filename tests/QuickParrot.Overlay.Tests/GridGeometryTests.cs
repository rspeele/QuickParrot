using System.Drawing;

namespace QuickParrot.Overlay.Tests;

public sealed class GridGeometryTests
{
    [Fact]
    public void Cells_AreColumnMajor()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(27), 1);

        // Items come out column by column, each column top to bottom.
        Assert.Equal(Enumerable.Range(1, 27).Select(n => $"Entry {n}"), layout.Items.Select(i => i.Name));
        var columns = layout.Items.Chunk(9).ToList();
        foreach (var column in columns)
        {
            Assert.Single(column.Select(c => c.Bounds.X).Distinct());
            Assert.Equal(column.Select(c => c.Bounds.Y).Order(), column.Select(c => c.Bounds.Y));
            Assert.Equal(Enumerable.Range(1, 9), column.Select(c => c.Number));
        }

        Assert.True(columns[1][0].Bounds.Left > columns[0][0].Bounds.Right);
        Assert.Equal(columns[0][0].Bounds.Y, columns[1][0].Bounds.Y);
    }

    [Fact]
    public void PartialLastColumn_HasOnlyItsEntries()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(40), 1);

        Assert.Equal(40, layout.Items.Count);
        Assert.Equal([1, 2, 3, 4, 5], layout.Headers.Select(h => h.Number));
        var lastColumnX = layout.Headers[4].Bounds.X;
        Assert.Equal(4, layout.Items.Count(i => i.Bounds.X == lastColumnX));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(81)]
    public void CellsAndHeaders_FitTheCanvasWithoutOverlapping(int count)
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(count, "Big"), 1.5f);
        var canvas = new RectangleF(PointF.Empty, layout.CanvasSize);
        var boxes = layout.Items.Select(i => i.Bounds).Concat(layout.Headers.Select(h => h.Bounds)).ToList();

        Assert.All(boxes, b => Assert.True(canvas.Contains(b)));
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
                Assert.False(boxes[i].IntersectsWith(boxes[j]), $"boxes {i} and {j} overlap");
        }

        Assert.All(layout.Items, i => Assert.True(i.Bounds.Contains(i.NameBounds)));
    }

    [Fact]
    public void Unzoomed_AllColumnsProminent_NamesOnly()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(40, "Big"), 1);

        Assert.All(layout.Headers, h => Assert.False(h.Dimmed));
        Assert.All(layout.Items, i => Assert.False(i.Dimmed || i.Highlighted));
        Assert.All(layout.Items, i => Assert.True(i.NumberBounds.IsEmpty));
        Assert.DoesNotContain(layout.Panels, p => p.Style == OverlayPanelStyle.HighlightedColumn);
        Assert.Equal(OverlayText.UpAction, layout.Hint?.Action);
    }

    [Fact]
    public void Zoomed_HighlightsThatColumnWithNumbers_AndDimsTheRest()
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(40, zoomedColumn: 2), 1);
        var zoomedX = layout.Headers[1].Bounds.X;
        var (zoomed, others) = (layout.Items.Where(i => i.Bounds.X == zoomedX).ToList(), layout.Items.Where(i => i.Bounds.X != zoomedX).ToList());

        Assert.Equal(9, zoomed.Count);
        Assert.All(zoomed, i => Assert.True(i.Highlighted && !i.Dimmed && !i.NumberBounds.IsEmpty));
        Assert.All(others, i => Assert.True(i.Dimmed && !i.Highlighted && i.NumberBounds.IsEmpty));
        Assert.All(layout.Headers.Where(h => h.Number != 2), h => Assert.True(h.Dimmed));

        var highlight = Assert.Single(layout.Panels, p => p.Style == OverlayPanelStyle.HighlightedColumn).Bounds;
        Assert.All(zoomed, i => Assert.True(highlight.Contains(i.Bounds)));
        Assert.All(zoomed, i => Assert.True(i.NameBounds.Left >= i.NumberBounds.Right));
    }

    [Fact]
    public void Hints_UpWhenNested_BackWhenZoomed_NoneAtRoot()
    {
        Assert.Null(OverlayLayoutGeometry.Compute(ViewStates.Grid(12), 1).Hint);
        Assert.Equal(OverlayText.UpAction, OverlayLayoutGeometry.Compute(ViewStates.Grid(12, "A/B"), 1).Hint?.Action);
        Assert.Equal(OverlayText.BackAction, OverlayLayoutGeometry.Compute(ViewStates.Grid(12, zoomedColumn: 1), 1).Hint?.Action);
        Assert.Equal(OverlayText.HintKey, OverlayLayoutGeometry.Compute(ViewStates.Grid(12, "A/B"), 1).Hint?.Key);
        Assert.Equal("B", OverlayLayoutGeometry.Compute(ViewStates.Grid(12, "A/B"), 1).Title.Text);
    }

    [Fact]
    public void Truncated_AddsANote()
    {
        Assert.Null(OverlayLayoutGeometry.Compute(ViewStates.Grid(81), 1).Note);
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Grid(100), 1);

        Assert.Contains("81", layout.Note?.Text);
        Assert.True(layout.Note!.Bounds.Top >= layout.Items.Max(i => i.Bounds.Bottom));
        Assert.True(layout.Note.Bounds.Bottom <= layout.CanvasSize.Height);
    }
}
