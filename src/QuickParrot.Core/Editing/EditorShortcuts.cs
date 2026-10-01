namespace QuickParrot.Core.Editing;

/// <summary>The keys the clip editor has shortcuts for; everything else is <see cref="Other"/>.</summary>
public enum EditorKey
{
    Other,
    Space,
    Enter,
    Escape,
    OpenBracket,
    CloseBracket,
    A,
    E,
    S,
    Y,
    Z,
}

/// <summary>Same values as WPF's ModifierKeys.</summary>
[Flags]
public enum EditorModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public enum EditorShortcut
{
    /// <summary>Not a shortcut: let the key through.</summary>
    None,

    /// <summary>Swallow the key without doing anything (a held key's auto-repeat).</summary>
    Swallow,
    TogglePlay,
    Save,
    Close,
    SelectAll,
    SetSelectionStart,
    SetSelectionEnd,
    PlayAndFocusName,
    SuggestName,
    Undo,
    Redo,
}

/// <summary>The clip editor window's keyboard shortcuts.</summary>
public static class EditorShortcuts
{
    /// <param name="typing">Focus is in a text box.</param>
    /// <param name="dropDownOpen">Focus is in a combo box with its dropdown open, which owns Enter and Esc.</param>
    public static EditorShortcut Map(EditorKey key, EditorModifiers modifiers, bool typing, bool isRepeat, bool dropDownOpen)
    {
        var control = modifiers == EditorModifiers.Control;
        var plain = !typing && modifiers == EditorModifiers.None;
        return key switch
        {
            // Holding a key mustn't toggle play, save, re-edit the selection, replay+refocus, or re-suggest repeatedly.
            EditorKey.Space or EditorKey.Enter or EditorKey.OpenBracket or EditorKey.CloseBracket or EditorKey.S or EditorKey.E when isRepeat =>
                !typing || key == EditorKey.Enter || (key is EditorKey.S or EditorKey.E && control) ? EditorShortcut.Swallow : EditorShortcut.None,
            EditorKey.Space when plain => EditorShortcut.TogglePlay,
            EditorKey.Enter when !dropDownOpen => EditorShortcut.Save,
            EditorKey.Escape when !dropDownOpen => EditorShortcut.Close,
            EditorKey.A when !typing && control => EditorShortcut.SelectAll,
            EditorKey.OpenBracket when plain => EditorShortcut.SetSelectionStart,
            EditorKey.CloseBracket when plain => EditorShortcut.SetSelectionEnd,
            // Both work regardless of focus, including from inside the name box itself.
            EditorKey.S when control => EditorShortcut.PlayAndFocusName,
            EditorKey.E when control => EditorShortcut.SuggestName,
            EditorKey.Z when !typing && control => EditorShortcut.Undo,
            EditorKey.Z when !typing && modifiers == (EditorModifiers.Control | EditorModifiers.Shift) => EditorShortcut.Redo,
            EditorKey.Y when !typing && control => EditorShortcut.Redo,
            _ => EditorShortcut.None,
        };
    }
}
