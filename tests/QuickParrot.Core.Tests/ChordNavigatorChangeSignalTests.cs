using QuickParrot.Core.Navigation;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests;

public class ChordNavigatorChangeSignalTests
{
    [Fact]
    public void ShiftNavigation_RaisesPersistentPathChanged()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);
        var changes = new List<string>();
        nav.PersistentPathChanged += changes.Add;

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, true));
        nav.Handle(new DigitPressed(0, true));

        Assert.Equal([trump, ""], changes);
    }

    [Fact]
    public void NonShiftNavigation_DoesNotRaise()
    {
        var source = new FakeFolderSource();
        source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source);
        var changes = new List<string>();
        nav.PersistentPathChanged += changes.Add;

        nav.Handle(new ChordPressed());
        nav.Handle(new DigitPressed(1, false));
        nav.Handle(new ChordReleased());

        Assert.Empty(changes);
    }

    [Fact]
    public void SettingSameValue_DoesNotRaise()
    {
        var nav = new ChordNavigator(new FakeFolderSource(), "Trump");
        var changes = new List<string>();
        nav.PersistentPathChanged += changes.Add;

        nav.PersistentPath = "Trump/";

        Assert.Empty(changes);
    }

    [Fact]
    public void FallbackFromMissingFolder_Raises()
    {
        var source = new FakeFolderSource();
        var trump = source.AddFolder("", "Trump");
        var nav = new ChordNavigator(source, trump);
        var changes = new List<string>();
        nav.PersistentPathChanged += changes.Add;
        source.RemoveFolder("", "Trump");

        nav.Handle(new ChordPressed());

        Assert.Equal([""], changes);
    }
}
