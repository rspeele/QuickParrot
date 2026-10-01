using QuickParrot.Core.Editing;

namespace QuickParrot.App.Editor;

/// <summary>What the clip editor needs besides the audio and its preview/encoder services.</summary>
public sealed record ClipEditorOptions
{
    /// <summary>The sound library's root folder; clips can only be saved inside it.</summary>
    public required string LibraryRoot { get; init; }

    /// <summary>The destination folder selected at first, "/"-separated relative to the root ("" for the root).</summary>
    public string InitialFolder { get; init; } = "";

    public LoudnessOptions Loudness { get; init; } = new();

    /// <summary>Optional AI namer: given the selected audio, returns a short name or null. Enables "Suggest name".</summary>
    public Func<EditableAudio, CancellationToken, Task<string?>>? SuggestName { get; init; }

    public TimeSpan SuggestDelay { get; init; } = NameSuggestionDebouncer.DefaultDelay;

    /// <summary>Whether releasing a selection-edge drag plays a 1 s sample of that edge.</summary>
    public bool PlaySampleOnDrag { get; init; } = true;

    /// <summary>Called when the user toggles <see cref="PlaySampleOnDrag"/>, so the app can persist it.</summary>
    public Action<bool>? PlaySampleOnDragChanged { get; init; }
}

public enum ClipEditorOutcome
{
    /// <summary>Closed with Done, Esc or the window's close button; the capture may be kept for later.</summary>
    Done,

    /// <summary>The user asked to throw the capture away (clips already saved are kept).</summary>
    Discarded,
}
