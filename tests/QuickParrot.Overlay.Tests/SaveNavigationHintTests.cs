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
    public void HintFitsAboveItems_AndModifierChangesKeepGeometryStable(int count, SmallFolderLayout small, float scale)
    {
        var state = (count > 9 ? ViewStates.Grid(count, "Quotes") : ViewStates.Wheel(count, "Quotes")) with
        {
            SmallFolderLayout = small,
            ShowSaveNavigationHint = true,
        };
        var inactive = OverlayLayoutGeometry.Compute(state, scale);
        var active = OverlayLayoutGeometry.Compute(state with { ShiftHeld = true }, scale);
        var hint = Assert.IsType<OverlaySaveNavigationHint>(inactive.SaveNavigationHint);

        Assert.False(hint.Active);
        Assert.True(active.SaveNavigationHint!.Active);
        Assert.Equal(hint.Label, active.SaveNavigationHint.Label);
        Assert.Equal(inactive.CanvasSize, active.CanvasSize);
        Assert.Equal(inactive.Items, active.Items);
        Assert.Equal(inactive.Panels, active.Panels);
        Assert.Equal(inactive.Headers, active.Headers);
        Assert.Equal("Shift: Save Navigation", hint.Label.Text);
        Assert.True(inactive.Panels[0].Bounds.Contains(hint.Label.Bounds));
        Assert.False(hint.Label.Bounds.IntersectsWith(inactive.Title.Bounds));
        Assert.All(inactive.Items, i => Assert.False(i.Bounds.IntersectsWith(hint.Label.Bounds)));
        Assert.All(inactive.Headers, h => Assert.False(h.Bounds.IntersectsWith(hint.Label.Bounds)));
        if (inactive.Hint is { } up)
            Assert.False(up.Bounds.IntersectsWith(hint.Label.Bounds));
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
