using QuickParrot.Core.Dsp;

namespace QuickParrot.Core.Editing;

/// <summary>
/// The editor's state and actions independent of any UI: the selection and cursor over one capture, what preview
/// should play, its loudness, and saving any number of clips from it.
/// </summary>
public sealed class ClipEditorSession
{
    public static readonly TimeSpan MinSelectionLength = TimeSpan.FromMilliseconds(50);

    private readonly IClipEncoder _encoder;
    private volatile MeasuredLoudness? _measured;

    public ClipEditorSession(EditableAudio audio, IClipEncoder encoder, LoudnessOptions loudness)
    {
        Audio = audio;
        _encoder = encoder;
        Loudness = loudness;
        Peaks = new WaveformPeaks(audio);
        MinSelectionFrames = Math.Min(audio.FramesFor(MinSelectionLength), audio.FrameCount);
        Selection = audio.FrameCount > 0 ? SilenceTrimmer.Suggest(audio) : ClipSelection.All(0);
        Cursor = Selection.Start;
    }

    public EditableAudio Audio { get; }

    public WaveformPeaks Peaks { get; }

    public LoudnessOptions Loudness { get; }

    public int MinSelectionFrames { get; }

    public ClipSelection Selection { get; private set; }

    public int Cursor { get; private set; }

    public bool CanSave => Selection.Length >= MinSelectionFrames && Selection.Length > 0;

    /// <summary>Sets the selection, clamped and at least the minimum length; snapping moves its edges to nearby quiet points.</summary>
    public ClipSelection Select(ClipSelection selection, bool snap)
    {
        var clamped = ClipSelection.FromPoints(selection.Start, selection.End, Audio.FrameCount, MinSelectionFrames);
        Selection = snap ? QuietPointSnapper.Snap(Audio, clamped, QuietPointSnapper.DefaultRadius, MinSelectionFrames) : clamped;
        return Selection;
    }

    public void SelectAll() => Selection = ClipSelection.All(Audio.FrameCount);

    public void SetCursor(int frame) => Cursor = Math.Clamp(frame, 0, Audio.FrameCount);

    /// <summary>The selection, or (see <see cref="PlaybackPlanner.FromCursorRange"/>) from the cursor.</summary>
    public ClipSelection PreviewRange(bool selectionOnly) =>
        selectionOnly && Selection.Length > 0 ? Selection : PlaybackPlanner.FromCursorRange(Selection, Cursor, Audio.FrameCount);

    /// <summary>The selection's integrated loudness, cached per selection. CPU-bound; fine on a worker thread.</summary>
    public double MeasureSelection(ClipSelection selection)
    {
        if (_measured is { } cached && cached.Selection == selection)
            return cached.Lufs;

        var lufs = LoudnessMeter.IntegratedLufs(Audio.Frames(selection.Start, selection.End), Audio.Channels, Audio.SampleRate);
        _measured = new MeasuredLoudness(selection, lufs);
        return lufs;
    }

    public float PreviewGain(double measuredLufs, bool normalize) =>
        normalize ? (float)Decibels.ToAmplitude(LoudnessNormalizer.GainDbFor(measuredLufs, Loudness)) : 1f;

    public EditableAudio SelectionAudio() => Audio.Slice(Selection.Start, Selection.End);

    /// <summary>Where the selection should move after saving <paramref name="saved"/>; see <see cref="PlaybackPlanner.PostSaveSelection"/>.</summary>
    public ClipSelection PostSaveSelection(ClipSelection saved) =>
        PlaybackPlanner.PostSaveSelection(saved, Audio.FrameCount, MinSelectionFrames);

    /// <summary>Renders <paramref name="selection"/> and saves it as <paramref name="name"/> in <paramref name="folder"/>. Blocking.</summary>
    public SavedClip Save(ClipSelection selection, string? name, string folder, bool normalize, CancellationToken cancellationToken)
    {
        if (selection.Length < Math.Max(1, MinSelectionFrames))
            throw new InvalidOperationException("Select some audio to save first.");

        var rendered = ClipRenderer.Render(Audio, selection, new ClipRenderOptions(normalize, Loudness));
        cancellationToken.ThrowIfCancellationRequested();
        return _encoder.Save(rendered, folder, ClipFileNames.Sanitize(name), cancellationToken);
    }

    private sealed record MeasuredLoudness(ClipSelection Selection, double Lufs);
}
