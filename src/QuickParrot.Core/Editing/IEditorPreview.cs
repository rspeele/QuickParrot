namespace QuickParrot.Core.Editing;

/// <summary>
/// Plays editor audio to the user's own monitor output only, never the virtual cable. Methods are called from the UI
/// thread; <see cref="Stopped"/> may be raised on any thread.
/// </summary>
public interface IEditorPreview
{
    /// <summary>
    /// Raised once with the previewed audio when a preview ends by itself or fails (with the error), but not after
    /// <see cref="Stop"/>.
    /// </summary>
    event Action<EditableAudio, Exception?>? Stopped;

    /// <summary>The audio being previewed now, so each editor window can tell its own preview from another's.</summary>
    EditableAudio? PlayingAudio { get; }

    /// <summary>The frame of the source audio being heard now, or null when nothing is playing.</summary>
    int? PositionFrame { get; }

    /// <summary>Opens the output (off the calling thread) and plays frames [start, end) at <paramref name="gain"/>, replacing any preview.</summary>
    Task PlayAsync(EditableAudio audio, int startFrame, int endFrame, float gain, CancellationToken cancellationToken);

    void Stop();
}
