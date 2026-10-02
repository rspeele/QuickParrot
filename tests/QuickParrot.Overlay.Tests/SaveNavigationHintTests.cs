using System.Drawing;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay.Tests;

public sealed class SaveNavigationHintTests
{
    [Theory]
    [InlineData(18, SmallFolderLayout.List, 1f)]
    [InlineData(18, SmallFolderLayout.List, 1.37f)]
    [InlineData(9, SmallFolderLayout.List, 1f)]
    [InlineData(9, SmallFolderLayout.Ring, 1.37f)]
    [InlineData(0, SmallFolderLayout.List, 2f)]
    [InlineData(0, SmallFolderLayout.Ring, 1f)]
    public void HintsShareTheTitleRow_AndVisibilityChangesKeepGeometryStable(int count, SmallFolderLayout small, float scale)
    {
        var state = (count > 9 ? ViewStates.Grid(count, "Quotes") : ViewStates.Wheel(count, "Quotes")) with
        {
            SmallFolderLayout = small,
            ShowSaveNavigationHint = true,
        };
        var inactive = OverlayLayoutGeometry.Compute(state, scale);
        var active = OverlayLayoutGeometry.Compute(state with { ShiftHeld = true }, scale);
        var absent = OverlayLayoutGeometry.Compute(state with { ShowSaveNavigationHint = false }, scale);
        var root = OverlayLayoutGeometry.Compute(state with { FolderPath = "", ShowSaveNavigationHint = false }, scale);
        var hint = Assert.IsType<OverlaySaveNavigationHint>(inactive.SaveNavigationHint);

        Assert.False(hint.Active);
        Assert.True(active.SaveNavigationHint!.Active);
        Assert.Equal(hint.Label, active.SaveNavigationHint.Label);
        foreach (var variant in new[] { active, absent, root })
        {
            Assert.Equal(inactive.CanvasSize, variant.CanvasSize);
            Assert.Equal(inactive.Items, variant.Items);
            Assert.Equal(inactive.Panels, variant.Panels);
            Assert.Equal(inactive.Headers, variant.Headers);
            Assert.Equal(inactive.Title.Bounds, variant.Title.Bounds);
            Assert.Equal(OverlayTextAlign.Near, variant.Title.Align);
        }
        Assert.Equal("Shift: Save Navigation", hint.Label.Text);
        Assert.Equal(OverlayTextAlign.Center, hint.Label.Align);
        Assert.Equal(inactive.Title.Bounds.Top, hint.Label.Bounds.Top);
        Assert.Equal(inactive.Title.Bounds.Height, hint.Label.Bounds.Height);
        Assert.True(inactive.Panels[0].Bounds.Contains(hint.Label.Bounds));
        Assert.False(hint.Label.Bounds.IntersectsWith(inactive.Title.Bounds));
        Assert.All(inactive.Items, i => Assert.False(i.Bounds.IntersectsWith(hint.Label.Bounds)));
        Assert.All(inactive.Headers, h => Assert.False(h.Bounds.IntersectsWith(hint.Label.Bounds)));
        if (inactive.Hint is { } up)
        {
            Assert.False(up.Bounds.IntersectsWith(hint.Label.Bounds));
            Assert.Equal(OverlayTextAlign.Far, up.Align);
            Assert.Equal(inactive.Title.Bounds.Top, up.Bounds.Top);
            Assert.True(inactive.Title.Bounds.Right < hint.Label.Bounds.Left);
            Assert.True(hint.Label.Bounds.Right < up.Bounds.Left);
        }
        if (inactive.Subtitle is { } subtitle)
            Assert.False(subtitle.Bounds.IntersectsWith(hint.Label.Bounds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenAtStartingFolder_AndDuringFavoritesAssignment(bool assigning)
    {
        var state = ViewStates.Grid(18) with
        {
            ShowSaveNavigationHint = assigning,
            ShiftHeld = true,
            Favorites = assigning ? ViewStates.Favorites(1) : null,
        };

        Assert.Null(OverlayLayoutGeometry.Compute(state, 1).SaveNavigationHint);
    }

    [Theory]
    [InlineData(SmallFolderLayout.List)]
    [InlineData(SmallFolderLayout.Ring)]
    public void HintCanBeShownAtRoot_WhenSessionStartedElsewhere(SmallFolderLayout small)
    {
        var layout = OverlayLayoutGeometry.Compute(ViewStates.Wheel(2) with
        {
            SmallFolderLayout = small,
            ShowSaveNavigationHint = true,
            ShiftHeld = true,
        }, 1);

        Assert.NotNull(layout.SaveNavigationHint);
        Assert.Null(layout.Hint);
        Assert.True(new RectangleF(PointF.Empty, layout.CanvasSize).Contains(layout.SaveNavigationHint!.Label.Bounds));
    }
}
