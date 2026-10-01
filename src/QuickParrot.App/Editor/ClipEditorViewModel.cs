using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
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
    private static readonly LibraryFolder BrowseEntry = new("Browse…", "Browse…", 0);

    private readonly IEditorPreview _preview;
    private readonly ClipEditorOptions _options;
    private readonly NameSuggestionDebouncer? _namer;
    private readonly DispatcherTimer _playheadTimer;
    private readonly Dispatcher _dispatcher;
    private readonly SelectionHistory _history;
    private ClipSelection _selection;
    private int _cursorFrame;
    private double _playheadFrame = double.NaN;
    private double _stopMarkerFrame = double.NaN;
    private bool _isPlaying;
    private bool _normalize = true;
    private bool _playSampleOnDrag;
    private double? _selectionLufs;
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
        Session = new ClipEditorSession(audio, encoder, options.Loudness);
        _selection = Session.Selection;
        _history = new SelectionHistory(_selection);
        _cursorFrame = Session.Cursor;
        _playSampleOnDrag = options.PlaySampleOnDrag;
        _name = string.IsNullOrWhiteSpace(audio.SuggestedTitle) ? "" : ClipFileNames.Sanitize(audio.SuggestedTitle);

        _libraryFolders = LibraryFolderList.Build(new FileSystemFolderSource(options.LibraryRoot));
        Folders = [.. _libraryFolders, BrowseEntry];
        _selectedFolder = LibraryFolderList.Find(_libraryFolders, options.InitialFolder);

        if (options.SuggestName is { } suggest)
            _namer = new NameSuggestionDebouncer(suggest);

        _playheadTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher) { Interval = PlayheadInterval };
        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        _preview.Stopped += OnPreviewStopped;

        MeasureSelection();
    }

    /// <summary>Raised after each clip is saved.</summary>
    public event Action<SavedClip>? ClipSaved;

    /// <summary>Raised when "Browse…" is picked, with the folder a picker dialog should start in; the view owns the dialog.</summary>
    public event Action<string>? FolderBrowseRequested;

    public ClipEditorSession Session { get; }

    public WaveformPeaks Peaks => Session.Peaks;

    public int SampleRate => Session.Audio.SampleRate;

    public int MinSelectionFrames => Session.MinSelectionFrames;

    public string Title => Session.Audio.SourceLabel is { Length: > 0 } label ? $"Edit clip — {label}" : "Edit clip";

    public string HeaderText => Session.Audio.SourceLabel is { Length: > 0 } label
        ? $"{label}  ·  {TimeFormatting.Position(Session.Audio.Duration.TotalSeconds)} captured"
        : $"{TimeFormatting.Position(Session.Audio.Duration.TotalSeconds)} captured";

    public ClipSelection Selection
    {
        get => _selection;
        set => ApplySelection(Session.Select(value, snap: false));
    }

    public int CursorFrame
    {
        get => _cursorFrame;
        set
        {
            Session.SetCursor(value);
            SetField(ref _cursorFrame, Session.Cursor);
        }
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

    public string SelectionStartText => TimeFormatting.Position(Session.Audio.SecondsAt(_selection.Start));

    public string SelectionEndText => TimeFormatting.Position(Session.Audio.SecondsAt(_selection.End));

    public string SelectionLengthText => TimeFormatting.Position(Session.Audio.SecondsAt(_selection.Length));

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
    public string LoudnessText
    {
        get
        {
            if (!_normalize)
                return "";
            if (_selectionLufs is not { } lufs)
                return "Measuring…";
            if (!double.IsFinite(lufs))
                return "Selection is silent";

            var gain = LoudnessNormalizer.GainDbFor(lufs, _options.Loudness);
            if (Math.Abs(gain) < 0.5)
                return "Already about right";

            var direction = gain > 0 ? "turned up" : "turned down";
            return string.Create(CultureInfo.CurrentCulture, $"Will be {direction} {Math.Abs(gain):0.#} dB");
        }
    }

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

    public bool CanSave => Session.CanSave && !_isSaving;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ObservableCollection<SavedClip> SavedClips { get; } = [];

    public ClipEditorOutcome Outcome { get; set; } = ClipEditorOutcome.Done;

    /// <summary>
    /// Called when a selection drag ends: snaps its edges to quiet points, re-measures, and (if enabled) plays a
    /// 1 s sample of the edge the drag touched. Naming is manual — see <see cref="SuggestNameAsync"/>.
    /// </summary>
    public void CommitSelection(SelectionDragTarget target, int anchorFrame)
    {
        var snapped = Session.Select(_selection, snap: true);
        ApplySelection(snapped);
        _history.Push(snapped);
        MeasureSelection();
        if (_playSampleOnDrag)
            _ = PlayAsync(PlaybackPlanner.DragReleaseSampleRange(snapped, target, anchorFrame, OneSecondFrames));
    }

    public void SelectAll()
    {
        Session.SelectAll();
        ApplySelection(Session.Selection);
        _history.Push(_selection);
        MeasureSelection();
    }

    /// <summary>Ctrl+Z: steps back to the previous selection, if any. Re-measures but doesn't play a sample or suggest a name.</summary>
    public void Undo() => ApplyHistorySelection(_history.Undo());

    /// <summary>Ctrl+Y / Ctrl+Shift+Z: re-applies the selection undone most recently, if any.</summary>
    public void Redo() => ApplyHistorySelection(_history.Redo());

    /// <summary>"[" or "]": sets that edge to the playhead (while playing) or the cursor (while stopped); see <see cref="PlaybackPlanner"/>.</summary>
    public void SetSelectionEdgeAtPlayheadOrCursor(bool isStart)
    {
        var target = IsPlaying && !double.IsNaN(PlayheadFrame) ? (int)Math.Round(PlayheadFrame) : _cursorFrame;
        var totalFrames = Session.Audio.FrameCount;
        var candidate = isStart
            ? PlaybackPlanner.SetSelectionStart(_selection, target, totalFrames)
            : PlaybackPlanner.SetSelectionEnd(_selection, target, totalFrames);

        ApplySelection(Session.Select(candidate, snap: true));
        _history.Push(_selection);
        MeasureSelection();
    }

    public Task TogglePlayAsync()
    {
        if (!IsPlaying && _playCts is null)
            return PlaySelectionAsync();

        Stop();
        return Task.CompletedTask;
    }

    public Task PlaySelectionAsync() => PlayAsync(Session.PreviewRange(selectionOnly: true));

    public Task PlayFromCursorAsync() => PlayAsync(Session.PreviewRange(selectionOnly: false));

    /// <summary>Plays the last second of the selection (or all of it if shorter).</summary>
    public Task PlayEndAsync() => PlayAsync(PlaybackPlanner.LastSeconds(Session.Selection, OneSecondFrames));

    /// <summary>Stops this editor's preview; another editor window's preview on the shared output is left playing.</summary>
    public void Stop() => StopInternal(manualStop: true);

    /// <summary>Suggest name button / Ctrl+E: requests a name now. No-op if naming isn't configured or already in flight.</summary>
    public async Task SuggestNameAsync()
    {
        if (_namer is null || _isSuggesting)
            return;

        IsSuggesting = true;
        _nameEditedByUser = false; // about to replace the name; typing while this is in flight should still win
        ApplySuggestion(await _namer.SuggestNowAsync(Session.SelectionAudio()));
    }

    public async Task SaveAsync()
    {
        if (!CanSave)
            return;

        SetSaving(true);
        var selection = _selection;
        var name = _name;
        var folder = FolderPath(_selectedFolder);
        var folderDisplay = _selectedFolder.Display;
        var normalize = _normalize;
        Status = "Saving…";
        SavedClip saved;
        try
        {
            saved = await Task.Run(() => Session.Save(selection, name, folder, normalize, CancellationToken.None));
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

        SavedClips.Add(saved);
        Status = saved.Warning is { } warning
            ? $"Saved “{saved.FileName}” to {folderDisplay}. {warning}"
            : $"Saved “{saved.FileName}” to {folderDisplay}. Select another bite, or click Done.";
        _namer?.Cancel();
        IsSuggesting = false;
        _name = "";
        _nameEditedByUser = false;
        OnPropertyChanged(nameof(Name));

        var next = Session.PostSaveSelection(selection);
        ApplySelection(Session.Select(next, snap: false));
        CursorFrame = _selection.Start;
        _history.Push(_selection);
        MeasureSelection();

        ClipSaved?.Invoke(saved);
    }

    /// <summary>Stops preview and any pending name suggestion; call when the editor closes.</summary>
    public void Dispose()
    {
        _preview.Stopped -= OnPreviewStopped;
        Stop();
        _namer?.Dispose();
    }

    private int OneSecondFrames => Session.Audio.SampleRate;

    private async Task PlayAsync(ClipSelection range)
    {
        StopInternal(manualStop: false);
        if (range.Length <= 0)
            return;

        var cts = new CancellationTokenSource();
        _playCts = cts;
        var selection = Session.Selection;
        try
        {
            var lufs = _selectionLufs ?? await Task.Run(() => Session.MeasureSelection(selection));
            await _preview.PlayAsync(Session.Audio, range.Start, range.End, Session.PreviewGain(lufs, _normalize), cts.Token);
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

    private bool OwnsPreview => _preview.PlayingAudio == Session.Audio;

    private void UpdatePlayhead()
    {
        if (OwnsPreview && _preview.PositionFrame is { } frame)
            PlayheadFrame = frame;
        else
            PlaybackEnded();
    }

    private void OnPreviewStopped(EditableAudio audio, Exception? error)
    {
        if (ReferenceEquals(audio, Session.Audio)) // the preview output is shared by every editor window
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

    private void ApplySelection(ClipSelection selection)
    {
        if (!SetField(ref _selection, selection, nameof(Selection)))
            return;

        OnPropertyChanged(nameof(SelectionStartText));
        OnPropertyChanged(nameof(SelectionEndText));
        OnPropertyChanged(nameof(SelectionLengthText));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Applies a selection popped off the undo history: re-measures only, no sample playback or name suggestion.</summary>
    private void ApplyHistorySelection(ClipSelection? selection)
    {
        if (selection is not { } value)
            return;

        ApplySelection(Session.Select(value, snap: false));
        MeasureSelection();
    }

    private async void MeasureSelection()
    {
        var version = ++_measureVersion;
        var selection = _selection;
        _selectionLufs = null;
        OnPropertyChanged(nameof(LoudnessText));
        double lufs;
        try
        {
            lufs = await Task.Run(() => Session.MeasureSelection(selection));
        }
        catch (Exception e) // async void: anything escaping would crash the app via the dispatcher
        {
            if (version == _measureVersion)
                Status = $"Couldn't measure loudness: {e.Message}";
            return;
        }

        if (version != _measureVersion)
            return;

        _selectionLufs = lufs;
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

    // Leaves the list (and the combo box's open dropdown) alone when nothing about the folders changed.
    private void RebuildFolders()
    {
        var latest = LibraryFolderList.Build(new FileSystemFolderSource(_options.LibraryRoot));
        if (latest.SequenceEqual(_libraryFolders))
            return;

        _libraryFolders = latest;
        Folders = [.. latest, BrowseEntry];
        OnPropertyChanged(nameof(Folders));
    }

    /// <summary>Called after a folder picked via <see cref="FolderBrowseRequested"/> comes back; null means the dialog was cancelled.</summary>
    public void ApplyBrowsedFolder(string? pickedFullPath)
    {
        if (pickedFullPath is null)
            return; // cancelled; the combo box already snapped back to the previous selection

        var relative = LibraryPathResolver.RelativePathWithin(_options.LibraryRoot, pickedFullPath);
        if (relative is null)
        {
            Status = $"Pick a folder inside your library ({_options.LibraryRoot}).";
            return;
        }

        RebuildFolders();
        var found = LibraryFolderList.Find(_libraryFolders, relative);
        SelectedFolder = found;
        if (!found.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase))
            Status = $"That folder isn't listed (hidden or nested too deep), so clips will go to {found.Display}.";
    }

    /// <summary>Call when the library changes on disk: rebuilds the folder list, keeping the selection if it still exists.</summary>
    public void RefreshFolders()
    {
        var previousPath = _selectedFolder.RelativePath;
        RebuildFolders();
        SelectedFolder = LibraryFolderList.Find(_libraryFolders, previousPath);
    }
}
