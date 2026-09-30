using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorCancelTests
{
    [Fact]
    public void Cancel_EndsSession_WithoutStoppingPlayback()
    {
        var nav = new ChordNavigator(new FakeFolderSource());

        nav.Handle(new ChordPressed());
        var actions = nav.Handle(new ChordCancelled());

        Assert.Empty(actions);
        Assert.Null(nav.ViewState);
        Assert.Empty(nav.Handle(new ChordReleased()));
    }

    [Fact]
    public void Cancel_KeepsPersistentNavigation_AndNextPressStartsThere()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, true));
        nav.Handle(new ChordCancelled());
        nav.Handle(new ChordPressed());

        Assert.Equal("Trump", nav.ViewState!.FolderPath);
    }

    [Fact]
    public void DigitsAfterCancel_AreIgnored()
    {
        var source = new FakeFolderSource();
        source.AddFile("", "clip.wav");
        var nav = new ChordNavigator(source);

        nav.Handle(new ChordPressed());
        nav.Handle(new ChordCancelled());

        Assert.Empty(nav.Handle(new DigitPressed(1, false)));
    }

    [Fact]
    public void Cancel_WhileInactive_EmitsNothing()
    {
        Assert.Empty(new ChordNavigator(new FakeFolderSource()).Handle(new ChordCancelled()));
    }
}
