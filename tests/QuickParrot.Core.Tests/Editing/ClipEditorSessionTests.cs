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
    public void StartsWithTheSilenceTrimmedSelection_AndTheCursorAtItsStart()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());

        Assert.Equal(SilenceTrimmer.Suggest(Capture), session.Selection);
        Assert.Equal(session.Selection.Start, session.Cursor);
        Assert.True(session.CanSave);
    }

    [Fact]
    public void Select_ClampsAndEnforcesTheMinimumLength()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());

        var selection = session.Select(new ClipSelection(-10, 100), snap: false);

        Assert.Equal(new ClipSelection(0, 2400), selection);
    }

    [Fact]
    public void PreviewRange_IsTheSelectionOrFromTheCursor()
    {
        var session = new ClipEditorSession(Capture, new FakeEncoder(), new LoudnessOptions());
        session.Select(new ClipSelection(1000, 9000), snap: false);
        session.SetCursor(5000);

        Assert.Equal(new ClipSelection(1000, 9000), session.PreviewRange(selectionOnly: true));
        // The cursor sits inside the selection, so "from cursor" stops at the selection end (see PlaybackPlannerTests
        // for the full range of cases, including the cursor outside the selection).
        Assert.Equal(new ClipSelection(5000, 9000), session.PreviewRange(selectionOnly: false));
        session.SetCursor(int.MaxValue);
        Assert.Equal(new ClipSelection(0, Capture.FrameCount), session.PreviewRange(selectionOnly: false));
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
        Assert.Equal(-18, LoudnessMeter.IntegratedLufs(audio), 0.2);
        Assert.Equal(0f, audio.Samples[0]);
        Assert.Equal("What No!.mp3", saved.FileName);
    }

    [Fact]
    public void Save_WithoutNormalization_KeepsTheOriginalLevel()
    {
        var encoder = new FakeEncoder();
        var session = new ClipEditorSession(Capture, encoder, new LoudnessOptions());

        session.Save(new ClipSelection(48000, 96000), "x", "f", normalize: false, CancellationToken.None);

        Assert.Equal(LoudnessMeter.IntegratedLufs(Capture.Slice(48000, 96000)), LoudnessMeter.IntegratedLufs(encoder.Saves[0].Audio), 0.05);
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

public class NameSuggesterTests
{
    private static readonly EditableAudio Clip = Audio(Silence(0.1));

    [Fact]
    public async Task SuggestNow_Sanitizes()
    {
        using var suggester = new NameSuggester((_, _) => Task.FromResult<string?>("\"Hasta la vista?\""));

        Assert.Equal(new NameSuggestion("Hasta la vista"), await suggester.SuggestNowAsync(Clip));
    }

    [Fact]
    public async Task Failures_AreReported_AndBlankNamesAreNull()
    {
        using var failing = new NameSuggester((_, _) => throw new HttpRequestException("offline"));
        using var blank = new NameSuggester((_, _) => Task.FromResult<string?>("  "));

        Assert.Equal(new NameSuggestion(null, "offline"), await failing.SuggestNowAsync(Clip));
        Assert.Equal(new NameSuggestion(null), await blank.SuggestNowAsync(Clip));
    }

    [Fact]
    public async Task ASecondRequest_CancelsTheFirst()
    {
        var firstRelease = new TaskCompletionSource<string?>();
        CancellationToken firstToken = default;
        var callCount = 0;
        using var suggester = new NameSuggester((_, token) =>
        {
            if (++callCount == 1)
            {
                firstToken = token;
                return firstRelease.Task;
            }

            return Task.FromResult<string?>("Second");
        });

        var first = suggester.SuggestNowAsync(Clip);
        Assert.True(suggester.IsBusy);
        var second = await suggester.SuggestNowAsync(Clip); // immediately cancels the first
        firstRelease.SetResult("Too late");

        Assert.Null(await first);
        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(new NameSuggestion("Second"), second);
    }

    [Fact]
    public async Task Cancel_AbandonsAnInFlightSuggestion()
    {
        var release = new TaskCompletionSource<string?>();
        CancellationToken seen = default;
        using var suggester = new NameSuggester((_, token) =>
        {
            seen = token;
            return release.Task;
        });

        var pending = suggester.SuggestNowAsync(Clip);
        Assert.True(suggester.IsBusy);
        suggester.Cancel();
        release.SetResult("Too late");

        Assert.Null(await pending);
        Assert.True(seen.IsCancellationRequested);
        Assert.False(suggester.IsBusy);
    }

    [Fact]
    public async Task ACancelledRequestFinishingLate_LeavesANewerOneBusy()
    {
        var releases = new List<TaskCompletionSource<string?>>();
        using var suggester = new NameSuggester((_, _) =>
        {
            var release = new TaskCompletionSource<string?>();
            releases.Add(release);
            return release.Task;
        });

        var first = suggester.SuggestNowAsync(Clip);
        suggester.Cancel(); // e.g. a save
        var second = suggester.SuggestNowAsync(Clip);
        releases[0].SetResult("Too late");

        Assert.Null(await first);
        Assert.True(suggester.IsBusy);
        releases[1].SetResult("Fresh");
        Assert.Equal(new NameSuggestion("Fresh"), await second);
        Assert.False(suggester.IsBusy);
    }
}
