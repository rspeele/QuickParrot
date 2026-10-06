using QuickParrot.Core.Editing;
using static QuickParrot.Core.Tests.Editing.TestSignals;

namespace QuickParrot.Core.Tests.Editing;

public class ClipEditorSessionTests
{
    private sealed class FakeEncoder : IClipEncoder
    {
        public List<(EditableAudio Audio, string Folder, string Stem)> Saves { get; } = [];

        public SavedClip Save(EditableAudio audio, string folder, string fileStem, CancellationToken cancellationToken)
        {
            Saves.Add((audio, folder, fileStem));
            return new SavedClip(Path.Combine(folder, fileStem + ".mp3"));
        }
    }

    private static readonly EditableAudio Capture =
        Audio(Concat(Silence(1), Sine(1000, -30, 1), Silence(1)));

    [Fact]
    public void InitialSelection_IsTheSilenceTrimmedCapture()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());

        Assert.Equal(SilenceTrimmer.Suggest(Capture), session.InitialSelection);
        Assert.True(session.CanSave(session.InitialSelection));
        Assert.False(session.CanSave(new ClipSelection(0, session.MinSelectionFrames - 1)));
    }

    [Fact]
    public void Constrain_ClampsAndEnforcesTheMinimumLength()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());

        Assert.Equal(new ClipSelection(0, 2400), session.Constrain(new ClipSelection(-10, 100), snap: false));
    }

    [Fact]
    public void ClampCursor_KeepsItInsideTheCapture()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());

        Assert.Equal(0, session.ClampCursor(-5));
        Assert.Equal(Capture.FrameCount, session.ClampCursor(int.MaxValue));
    }

    [Fact]
    public void PreviewRange_IsTheSelectionOrFromTheCursor()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());
        var selection = new ClipSelection(1000, 9000);

        Assert.Equal(selection, session.PreviewRange(selection, 5000, selectionOnly: true));
        // The cursor sits inside the selection, so "from cursor" stops at the selection end (see PlaybackPlannerTests
        // for the full range of cases, including the cursor outside the selection).
        Assert.Equal(new ClipSelection(5000, 9000), session.PreviewRange(selection, 5000, selectionOnly: false));
        Assert.Equal(new ClipSelection(0, Capture.FrameCount), session.PreviewRange(selection, Capture.FrameCount, selectionOnly: false));
    }

    [Fact]
    public void Save_RendersTheSelectionNormalizedUnderASanitizedName()
    {
        var encoder = new FakeEncoder();
        var session = new ClipEditorSession(Capture, encoder, new LoudnessOptions(TargetLufs: -18));
        var selection = new ClipSelection(48000, 96000);

        var saved = session.Save(selection, "What? No!", @"C:\Library\Movies", normalize: true, CancellationToken.None);

        var (audio, folder, stem) = Assert.Single(encoder.Saves);
        Assert.Equal(("What No!", @"C:\Library\Movies"), (stem, folder));
        Assert.Equal(48000, audio.FrameCount);
        Assert.Equal(-18, Lufs(audio), 0.2);
        Assert.Equal(0f, audio.Samples.Span[0]);
        Assert.Equal("What No!.mp3", saved.FileName);
    }

    [Fact]
    public void Save_WithoutNormalization_KeepsTheOriginalLevel()
    {
        var encoder = new FakeEncoder();
        var session = new ClipEditorSession(Capture, encoder, new LoudnessOptions());

        session.Save(new ClipSelection(48000, 96000), "x", "f", normalize: false, CancellationToken.None);

        Assert.Equal(Lufs(Audio(Capture.Frames(48000, 96000).ToArray())), Lufs(encoder.Saves[0].Audio), 0.05);
    }

    [Fact]
    public void MeasureAndPreviewGain_FollowTheTarget()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions(TargetLufs: -18));
        var lufs = session.MeasureSelection(new ClipSelection(48000, 96000));

        Assert.Equal(-30, lufs, 0.2); // a stereo sine reads its peak level
        Assert.Equal(DbToLinear(-18 - lufs), session.PreviewGain(lufs, normalize: true), 1e-4);
        Assert.Equal(1f, session.PreviewGain(lufs, normalize: false));
    }

    [Fact]
    public void PostSaveSelection_WithRoomLeft_MovesToTheEndOfTheCapture()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());

        var next = session.PostSaveSelection(new ClipSelection(0, Capture.FrameCount - session.MinSelectionFrames - 1));

        Assert.Equal(new ClipSelection(Capture.FrameCount - session.MinSelectionFrames - 1, Capture.FrameCount), next);
    }

    [Fact]
    public void PostSaveSelection_WithLittleLeft_KeepsTheSavedSelection()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());
        var saved = new ClipSelection(0, Capture.FrameCount);

        Assert.Equal(saved, session.PostSaveSelection(saved));
    }
}
