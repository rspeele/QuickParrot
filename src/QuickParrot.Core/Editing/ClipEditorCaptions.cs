namespace QuickParrot.Core.Editing;

/// <summary>The clip editor's window title and header line for a capture.</summary>
public static class ClipEditorCaptions
{
    public static string Title(EditableAudio audio) =>
        audio.SourceLabel is { Length: > 0 } label ? $"Edit clip — {label}" : "Edit clip";

    public static string Header(EditableAudio audio)
    {
        var captured = $"{TimeFormatting.Position(audio.Duration.TotalSeconds)} captured";
        return audio.SourceLabel is { Length: > 0 } label ? $"{label}  ·  {captured}" : captured;
    }
}
