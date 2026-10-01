using System.Windows.Threading;
using QuickParrot.App.Mvvm;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Library;

namespace QuickParrot.App.Editor;

/// <summary>
/// The trim editor's view model, a thin binding layer over <see cref="ClipEditorSession"/>: selection readouts, preview,
/// loudness display, naming (optionally AI-suggested) and saving. Create and use it on the UI thread.
/// </summary>
public sealed class ClipEditorViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PlayheadInterval = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// A sentinel "Browse…" row appended to <see cref="Folders"/>; selecting it opens a folder picker instead.
    /// Its RelativePath doubles as the display text (see <see cref="LibraryFolder.Display"/>), not a real path —
    /// intercepted by reference before it's ever used as one.
    /// </summary>
    private static readonly LibraryFolder BrowseEntry = new("Browse…");

    private readonly ClipEditorSession _session;
    private readonly IEditorPreview _preview;
    private readonly ClipEditorOptions _options;
    private readonly NameSuggester? _namer;
    private readonly DispatcherTimer _playheadTimer;
    private readonly Dispatcher _dispatcher;
    private SelectionHistory _state;
    private double _playheadFrame = double.NaN;
    private double _stopMarkerFrame = double.NaN;
    private bool _isPlaying;
    private bool _normalize = true;
    private bool _playSampleOnDrag;
    private LoudnessReading _loudness;
    private int _measureVersion;
    private string _name = "";
    private bool _nameEditedByUser;
    private bool _isSuggesting;
    private bool _isSaving;
    private string _status = "";
    private LibraryFolder _selectedFolder;
    private IReadOnlyList<LibraryFolder> _libraryFolders;
    private CancellationTokenSource? _playCts;

    public ClipEditorViewModel(EditableAudio audio, IEditorPreview preview, IClipEncoder encoder, ClipEditorOptions options)
    {
        _preview = preview;
        _options = options;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _session = new ClipEditorSession(audio, encoder, options.Loudness);
        _state = SelectionHistory.Start(_session.InitialSelection, _session.InitialSelection.Start);
        _playSampleOnDrag = options.PlaySampleOnDrag;
        _name = string.IsNullOrWhiteSpace(audio.SuggestedTitle) ? "" : ClipFileNames.Sanitize(audio.SuggestedTitle);

        _libraryFolders = options.Folders;
        Folders = [.. _libraryFolders, BrowseEntry];
        _selectedFolder = LibraryFolderList.Find(_libraryFolders, options.InitialFolder);

        if (options.SuggestName is { } suggest)
            _namer = new NameSuggester(suggest);

        _playheadTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher) { Interval = PlayheadInterval };
        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        _preview.Stopped += OnPreviewStopped;

        MeasureSelection();
    }

    /// <summary>Raised after each clip is saved.</summary>
    public event Action<SavedClip>? ClipSaved;

    /// <summary>Raised when "Browse…" is picked, with the folder a picker dialog should start in; the view owns the dialog.</summary>
    public event Action<string>? FolderBrowseRequested;

    /// <summary>How many clips this editor has saved so far.</summary>
    public int SavedClipCount { get; private set; }

    public WaveformPeaks Peaks => _session.Peaks;

    public int SampleRate => _session.Audio.SampleRate;

    public int MinSelectionFrames => _session.MinSelectionFrames;

    public string Title => ClipEditorCaptions.Title(_session.Audio);

    public string HeaderText => ClipEditorCaptions.Header(_session.Audio);

    /// <summary>Set by the waveform while dragging; a drag becomes an undo step only in <see cref="CommitSelection"/>.</summary>
    public ClipSelection Selection
    {
        get => _state.Current;
        set => SetState(_state.Preview(_session.Constrain(value, snap: false)));
    }

    public int CursorFrame
    {
        get => _state.Cursor;
        set => SetState(_state.WithCursor(_session.ClampCursor(value)));
    }

    public double PlayheadFrame
    {
        get => _playheadFrame;
        private set => SetField(ref _playheadFrame, value);
    }

    /// <summary>Where playback last stopped manually, for a faint marker in the waveform; NaN once there's no mark.</summary>
    public double StopMarkerFrame
    {
        get => _stopMarkerFrame;
        private set => SetField(ref _stopMarkerFrame, value);
    }

    public string SelectionStartText => TimeFormatting.Position(_session.Audio.SecondsAt(Selection.Start));

    public string SelectionEndText => TimeFormatting.Position(_session.Audio.SecondsAt(Selection.End));

    public string SelectionLengthText => TimeFormatting.Position(_session.Audio.SecondsAt(Selection.Length));

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetField(ref _isPlaying, value);
    }

    public bool Normalize
    {
        get => _normalize;
        set
        {
            if (SetField(ref _normalize, value))
                OnPropertyChanged(nameof(LoudnessText));
        }
    }

    public string NormalizeLabel => "Normalize to ordinary speaking volume";

    public bool PlaySampleOnDrag
    {
        get => _playSampleOnDrag;
        set
        {
            if (SetField(ref _playSampleOnDrag, value))
                _options.PlaySampleOnDragChanged?.Invoke(value);
        }
    }

    /// <summary>A plain-words description of the normalization gain; hidden (empty) unless <see cref="Normalize"/> is on.</summary>
    public string LoudnessText => _normalize ? LoudnessDescription.Describe(_loudness, _options.Loudness) : "";

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
                _nameEditedByUser = value.Length > 0;
        }
    }

    public bool CanSuggestName => _namer is not null && !_isSuggesting;

    public bool HasNamer => _namer is not null;

    public string SuggestButtonText => _isSuggesting ? "Suggesting…" : "Suggest name";

    public bool IsSuggesting
    {
        get => _isSuggesting;
        private set
        {
            if (SetField(ref _isSuggesting, value))
            {
                OnPropertyChanged(nameof(CanSuggestName));
                OnPropertyChanged(nameof(SuggestButtonText));
            }
        }
    }

    public IReadOnlyList<LibraryFolder> Folders { get; private set; }

    public LibraryFolder SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (ReferenceEquals(value, BrowseEntry))
            {
                OnPropertyChanged(nameof(SelectedFolder)); // the combo box picked "Browse…"; snap it back to the real selection
                var initialDirectory = FolderPath(_selectedFolder);
                _dispatcher.BeginInvoke(() => FolderBrowseRequested?.Invoke(initialDirectory)); // after the dropdown closes
                return;
            }

            SetField(ref _selectedFolder, value ?? Folders[0]);
        }
    }

    public bool CanSave => _session.CanSave(Selection) && !_isSaving;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>
    /// Called when a selection drag ends: snaps its edges to quiet points, records an undo step, re-measures, and (if
    /// enabled) plays a 1 s sample of the edge the drag touched. Naming is manual — see <see cref="SuggestNameAsync"/>.
    /// </summary>
    public void CommitSelection(SelectionDragTarget target, int anchorFrame)
    {
        var snapped = _session.Constrain(Selection, snap: true);
        SetState(_state.Push(snapped));
        MeasureSelection();
        if (_playSampleOnDrag)
            _ = PlayAsync(PlaybackPlanner.DragReleaseSampleRange(snapped, target, anchorFrame, OneSecondFrames));
    }

    public void SelectAll()
    {
        SetState(_state.Push(_session.AllFrames));
        MeasureSelection();
    }

    /// <summary>Ctrl+Z: steps back to the previous selection, if any. Re-measures but doesn't play a sample or suggest a name.</summary>
    public void Undo()
    {
        if (!_state.CanUndo)
            return;

        SetState(_state.Undo());
        MeasureSelection();
    }

    /// <summary>Ctrl+Y / Ctrl+Shift+Z: re-applies the selection undone most recently, if any.</summary>
    public void Redo()
    {
        if (!_state.CanRedo)
            return;

        SetState(_state.Redo());
        MeasureSelection();
    }

    /// <summary>"[" or "]": sets that edge to the playhead (while playing) or the cursor (while stopped); see <see cref="PlaybackPlanner"/>.</summary>
    public void SetSelectionEdgeAtPlayheadOrCursor(bool isStart)
    {
        var target = IsPlaying && !double.IsNaN(PlayheadFrame) ? (int)Math.Round(PlayheadFrame) : _state.Cursor;
        var totalFrames = _session.Audio.FrameCount;
        var candidate = isStart
            ? PlaybackPlanner.SetSelectionStart(Selection, target, totalFrames)
            : PlaybackPlanner.SetSelectionEnd(Selection, target, totalFrames);

        SetState(_state.Push(_session.Constrain(candidate, snap: true)));
        MeasureSelection();
    }

    public Task TogglePlayAsync()
    {
        if (!IsPlaying && _playCts is null)
            return PlaySelectionAsync();

        Stop();
        return Task.CompletedTask;
    }

    public Task PlaySelectionAsync() => PlayAsync(_session.PreviewRange(Selection, _state.Cursor, selectionOnly: true));

    public Task PlayFromCursorAsync() => PlayAsync(_session.PreviewRange(Selection, _state.Cursor, selectionOnly: false));

    /// <summary>Plays the last second of the selection (or all of it if shorter).</summary>
    public Task PlayEndAsync() => PlayAsync(PlaybackPlanner.LastSeconds(Selection, OneSecondFrames));

    /// <summary>Stops this editor's preview; another editor window's preview on the shared output is left playing.</summary>
    public void Stop() => StopInternal(manualStop: true);

    /// <summary>Suggest name button / Ctrl+E: requests a name now. No-op if naming isn't configured or already in flight.</summary>
    public async Task SuggestNameAsync()
    {
        if (_namer is null || _isSuggesting)
            return;

        IsSuggesting = true;
        _nameEditedByUser = false; // about to replace the name; typing while this is in flight should still win
        ApplySuggestion(await _namer.SuggestNowAsync(_session.SelectionAudio(Selection)));
    }

    public async Task SaveAsync()
    {
        if (!CanSave)
            return;

        SetSaving(true);
        var selection = Selection;
        var name = _name;
        var folder = FolderPath(_selectedFolder);
        var folderDisplay = _selectedFolder.Display;
        var normalize = _normalize;
        Status = "Saving…";
        SavedClip saved;
        try
        {
            saved = await Task.Run(() => _session.Save(selection, name, folder, normalize, CancellationToken.None));
        }
        catch (Exception e)
        {
            Status = $"Couldn't save: {e.Message}";
            return;
        }
        finally
        {
            SetSaving(false);
        }

        Status = saved.Warning is { } warning
            ? $"Saved “{saved.FileName}” to {folderDisplay}. {warning}"
            : $"Saved “{saved.FileName}” to {folderDisplay}. Select another bite, or click Done.";
        _namer?.Cancel();
        IsSuggesting = false;
        _name = "";
        _nameEditedByUser = false;
        OnPropertyChanged(nameof(Name));

        var next = _session.Constrain(_session.PostSaveSelection(selection), snap: false);
        SetState(_state.Push(next).WithCursor(_session.ClampCursor(next.Start)));
        MeasureSelection();

        SavedClipCount++;
        ClipSaved?.Invoke(saved);
    }

    /// <summary>Call when the library changes on disk: replaces the folder list, keeping the selection if it still exists.</summary>
    public void UpdateFolders(IReadOnlyList<LibraryFolder> latest)
    {
        var previousPath = _selectedFolder.RelativePath;
        if (!latest.SequenceEqual(_libraryFolders)) // leaves the combo box's open dropdown alone when nothing changed
        {
            _libraryFolders = latest;
            Folders = [.. latest, BrowseEntry];
            OnPropertyChanged(nameof(Folders));
        }

        SelectedFolder = LibraryFolderList.Find(_libraryFolders, previousPath);
    }

    /// <summary>Called after a folder picked via <see cref="FolderBrowseRequested"/> comes back; null means the dialog was cancelled.</summary>
    public async Task ApplyBrowsedFolderAsync(string? pickedFullPath)
    {
        if (pickedFullPath is null)
            return; // cancelled; the combo box already snapped back to the previous selection

        try
        {
            await _options.RefreshFolders(); // the dialog may have just created the folder
        }
        catch (Exception e) // the view's caller is async void
        {
            Status = $"Couldn't list the library's folders: {e.Message}";
        }

        var (folder, warning) = LibraryFolderList.ResolvePicked(_options.LibraryRoot, _libraryFolders, pickedFullPath);
        if (folder is not null)
            SelectedFolder = folder;
        if (warning is not null)
            Status = warning;
    }

    /// <summary>Stops preview and any pending name suggestion; call when the editor closes.</summary>
    public void Dispose()
    {
        _preview.Stopped -= OnPreviewStopped;
        Stop();
        _namer?.Dispose();
    }

    private int OneSecondFrames => _session.Audio.SampleRate;

    private async Task PlayAsync(ClipSelection range)
    {
        StopInternal(manualStop: false);
        if (range.Length <= 0)
            return;

        var cts = new CancellationTokenSource();
        _playCts = cts;
        var selection = Selection;
        try
        {
            var lufs = _loudness.Lufs ?? await Task.Run(() => _session.MeasureSelection(selection));
            await _preview.PlayAsync(_session.Audio, range.Start, range.End, _session.PreviewGain(lufs, _normalize), cts.Token);
            if (cts.IsCancellationRequested)
                return;

            IsPlaying = true;
            PlayheadFrame = range.Start;
            _playheadTimer.Start();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            Status = $"Can't preview: {e.Message}";
        }
        finally
        {
            if (_playCts == cts)
                _playCts = null;
            cts.Dispose();
        }
    }

    /// <summary>A manual stop (button/Space) marks the stop position, replacing any earlier mark; any other stop leaves it alone.</summary>
    private void StopInternal(bool manualStop)
    {
        StopMarkerFrame = PlaybackPlanner.StopMarker(_stopMarkerFrame, manualStop && IsPlaying && !double.IsNaN(PlayheadFrame), PlayheadFrame);
        _playCts?.Cancel();
        if (OwnsPreview)
            _preview.Stop();

        PlaybackEnded();
    }

    private bool OwnsPreview => _preview.PlayingAudio == _session.Audio;

    private void UpdatePlayhead()
    {
        if (OwnsPreview && _preview.PositionFrame is { } frame)
            PlayheadFrame = frame;
        else
            PlaybackEnded();
    }

    private void OnPreviewStopped(EditableAudio audio, Exception? error)
    {
        if (ReferenceEquals(audio, _session.Audio)) // the preview output is shared by every editor window
            _dispatcher.BeginInvoke(() => OnOwnPreviewStopped(error));
    }

    private void OnOwnPreviewStopped(Exception? error)
    {
        PlaybackEnded();
        if (error is not null)
            Status = $"Preview stopped: {error.Message}";
    }

    private void PlaybackEnded()
    {
        _playheadTimer.Stop();
        IsPlaying = false;
        PlayheadFrame = double.NaN;
    }

    private void SetState(SelectionHistory next)
    {
        var previous = _state;
        _state = next;
        if (previous.Current != next.Current)
        {
            OnPropertyChanged(nameof(Selection));
            OnPropertyChanged(nameof(SelectionStartText));
            OnPropertyChanged(nameof(SelectionEndText));
            OnPropertyChanged(nameof(SelectionLengthText));
            OnPropertyChanged(nameof(CanSave));
        }

        if (previous.Cursor != next.Cursor)
            OnPropertyChanged(nameof(CursorFrame));
    }

    private async void MeasureSelection()
    {
        var version = ++_measureVersion;
        var selection = Selection;
        SetLoudness(LoudnessReading.Measuring);
        double lufs;
        try
        {
            lufs = await Task.Run(() => _session.MeasureSelection(selection));
        }
        catch (Exception e) // async void: anything escaping would crash the app via the dispatcher
        {
            if (version == _measureVersion)
            {
                SetLoudness(LoudnessReading.Unavailable);
                Status = $"Couldn't measure loudness: {e.Message}";
            }

            return;
        }

        if (version == _measureVersion)
            SetLoudness(LoudnessReading.Of(lufs));
    }

    private void SetLoudness(LoudnessReading reading)
    {
        _loudness = reading;
        OnPropertyChanged(nameof(LoudnessText));
    }

    // A null result means a save or Dispose() cancelled it while in flight; nothing to report.
    private void ApplySuggestion(NameSuggestion? suggestion)
    {
        IsSuggesting = _namer?.IsBusy == true; // a request cancelled by a save may finish after a newer one started
        if (suggestion is null)
            return;

        if (suggestion.Error is { } error)
            Status = $"Couldn't suggest a name: {error}";
        else if (suggestion.Name is null)
            Status = "No name suggested.";
        else if (!_nameEditedByUser) // the user may have typed a name of their own while this was in flight
        {
            _name = suggestion.Name;
            OnPropertyChanged(nameof(Name));
        }
    }

    private void SetSaving(bool saving)
    {
        _isSaving = saving;
        OnPropertyChanged(nameof(CanSave));
    }

    private string FolderPath(LibraryFolder folder) => LibraryPathResolver.FullPath(_options.LibraryRoot, folder.RelativePath);
}
