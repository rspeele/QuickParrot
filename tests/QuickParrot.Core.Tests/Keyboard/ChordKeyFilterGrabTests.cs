using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Navigation;

namespace QuickParrot.Core.Tests.Keyboard;

public class ChordKeyFilterGrabTests
{
    private static readonly ScanKey B = ScanKey.DefaultChordKey;

    private readonly ChordKeyFilter _filter = new(B);

    public static TheoryData<ScanKey> EnterKeys => [ScanKey.Enter, ScanKey.NumpadEnter];

    [Theory]
    [MemberData(nameof(EnterKeys))]
    public void EnterDuringChord_IsSwallowed_AndEmitsGrab(ScanKey enter)
    {
        Down(B);

        Assert.Equal(new KeyFilterResult(true, new GrabPressed()), Down(enter));
        Assert.Equal(KeyFilterResult.SwallowSilently, Up(enter));
        Assert.True(_filter.ChordActive);
    }

    [Theory]
    [MemberData(nameof(EnterKeys))]
    public void EnterWithoutChord_PassesThrough(ScanKey enter)
    {
        Assert.Equal(KeyFilterResult.PassThrough, Down(enter));
        Assert.Equal(KeyFilterResult.PassThrough, Up(enter));
    }

    [Fact]
    public void EnterAutoRepeat_DoesNotGrabAgain()
    {
        Down(B);
        Down(ScanKey.Enter);

        Assert.Equal(KeyFilterResult.SwallowSilently, Down(ScanKey.Enter));
    }

    [Fact]
    public void EnterHeldPastChordRelease_UpIsStillHidden()
    {
        Down(B);
        Down(ScanKey.Enter);
        Assert.Equal(new KeyFilterResult(true, new ChordReleased()), Up(B));

        Assert.Equal(KeyFilterResult.SwallowSilently, Up(ScanKey.Enter));
    }

    [Fact]
    public void EnterHeldBeforeChord_StaysVisible()
    {
        Down(ScanKey.Enter);
        Down(B);

        Assert.Equal(KeyFilterResult.PassThrough, Down(ScanKey.Enter));
        Assert.Equal(KeyFilterResult.PassThrough, Up(ScanKey.Enter));
    }

    [Fact]
    public void Enter_CannotBeTheChordKey()
    {
        Assert.Throws<ArgumentException>(() => new ChordKeyFilter(ScanKey.Enter));
        Assert.Throws<ArgumentException>(() => new ChordKeyFilter(ScanKey.NumpadEnter));
    }

    private KeyFilterResult Down(ScanKey key) =>
        _filter.Process(key.ScanCode, key.IsExtended, isKeyDown: true, isInjected: false);

    private KeyFilterResult Up(ScanKey key) =>
        _filter.Process(key.ScanCode, key.IsExtended, isKeyDown: false, isInjected: false);
}
