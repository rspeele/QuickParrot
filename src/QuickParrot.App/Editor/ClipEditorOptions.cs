using QuickParrot.Core.Editing;

namespace QuickParrot.App.Editor;

/// <summary>What the clip editor needs besides the audio and its preview/encoder services.</summary>
public sealed record ClipEditorOptions
{
    /// <summary>The sound library's root folder; clips can only be saved inside it.</summary>
    public required string LibraryRoot { get; init; }

    /// <summary>The library's folders, already scanned (see <see cref="LibraryFolderList.Build"/>).</summary>
    public required IReadOnlyList<LibraryFolder> Folders { get; init; }

    /// <summary>Rescans the folders off the UI thread and hands them to <see cref="ClipEditorViewModel.UpdateFolders"/>.</summary>
    public required Func<Task> RefreshFolders { get; init; }

    /// <summary>The destination folder selected at first, "/"-separated relative to the root ("" for the root).</summary>
    public string InitialFolder { get; init; } = "";

    public LoudnessOptions Loudness { get; init; } = new();

    /// <summary>Optional AI namer: given the selected audio, a short name or why there's none. Enables "Suggest name".</summary>
    public Func<EditableAudio, CancellationToken, Task<NameSuggestion>>? SuggestName { get; init; }

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
