using QuickParrot.Core.Editing;
using static QuickParrot.Core.Editing.EditorKey;
using static QuickParrot.Core.Editing.EditorShortcut;

namespace QuickParrot.Core.Tests.Editing;

public class EditorShortcutsTests
{
    private const EditorModifiers NoMods = EditorModifiers.None;
    private const EditorModifiers Ctrl = EditorModifiers.Control;
    private const EditorModifiers CtrlShift = EditorModifiers.Control | EditorModifiers.Shift;

    private static EditorShortcut Map(EditorKey key, EditorModifiers mods = NoMods, bool typing = false, bool repeat = false, bool dropDown = false) =>
        EditorShortcuts.Map(key, mods, typing, repeat, dropDown);

    [Theory]
    [InlineData(Space, NoMods, TogglePlay)]
    [InlineData(Enter, NoMods, Save)]
    [InlineData(Enter, EditorModifiers.Shift, Save)]
    [InlineData(Escape, NoMods, Close)]
    [InlineData(A, Ctrl, SelectAll)]
    [InlineData(OpenBracket, NoMods, SetSelectionStart)]
    [InlineData(CloseBracket, NoMods, SetSelectionEnd)]
    [InlineData(S, Ctrl, PlayAndFocusName)]
    [InlineData(Z, Ctrl, Undo)]
    [InlineData(Z, CtrlShift, Redo)]
    [InlineData(Y, Ctrl, Redo)]
    [InlineData(Space, Ctrl, EditorShortcut.None)]
    [InlineData(A, NoMods, EditorShortcut.None)]
    [InlineData(OpenBracket, EditorModifiers.Shift, EditorShortcut.None)]
    [InlineData(S, CtrlShift, EditorShortcut.None)]
    [InlineData(Y, CtrlShift, EditorShortcut.None)]
    [InlineData(Other, Ctrl, EditorShortcut.None)]
    public void Map_OutsideATextBox(EditorKey key, EditorModifiers mods, EditorShortcut expected)
    {
        Assert.Equal(expected, Map(key, mods));
    }

    [Theory]
    [InlineData(Space, NoMods, EditorShortcut.None)]
    [InlineData(OpenBracket, NoMods, EditorShortcut.None)]
    [InlineData(CloseBracket, NoMods, EditorShortcut.None)]
    [InlineData(A, Ctrl, EditorShortcut.None)]
    [InlineData(Z, Ctrl, EditorShortcut.None)]
    [InlineData(Z, CtrlShift, EditorShortcut.None)]
    [InlineData(Y, Ctrl, EditorShortcut.None)]
    [InlineData(Enter, NoMods, Save)]
    [InlineData(Escape, NoMods, Close)]
    [InlineData(S, Ctrl, PlayAndFocusName)]
    public void Map_WhileTyping_LeavesEditingKeysToTheTextBox(EditorKey key, EditorModifiers mods, EditorShortcut expected)
    {
        Assert.Equal(expected, Map(key, mods, typing: true));
    }

    [Theory]
    [InlineData(Space, NoMods, false, Swallow)]
    [InlineData(Space, NoMods, true, EditorShortcut.None)]
    [InlineData(Enter, NoMods, false, Swallow)]
    [InlineData(Enter, NoMods, true, Swallow)]
    [InlineData(OpenBracket, NoMods, false, Swallow)]
    [InlineData(CloseBracket, NoMods, true, EditorShortcut.None)]
    [InlineData(S, Ctrl, true, Swallow)]
    [InlineData(S, NoMods, false, Swallow)]
    [InlineData(S, NoMods, true, EditorShortcut.None)]
    [InlineData(S, EditorModifiers.Shift, true, EditorShortcut.None)]
    [InlineData(Escape, NoMods, false, Close)]
    [InlineData(Z, Ctrl, false, Undo)]
    public void Map_HeldKeys_DoNotRepeatTheirAction(EditorKey key, EditorModifiers mods, bool typing, EditorShortcut expected)
    {
        Assert.Equal(expected, Map(key, mods, typing, repeat: true));
    }

    [Theory]
    [InlineData(Enter)]
    [InlineData(Escape)]
    public void Map_LeavesEnterAndEscToAnOpenDropDown(EditorKey key)
    {
        Assert.Equal(EditorShortcut.None, Map(key, dropDown: true));
    }

    [Fact]
    public void Map_SpaceStillTogglesPlayWithADropDownOpen()
    {
        Assert.Equal(TogglePlay, Map(Space, dropDown: true));
    }
}
