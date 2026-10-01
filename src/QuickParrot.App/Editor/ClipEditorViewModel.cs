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

    private readonly IEditorPreview _preview;
    private readonly ClipEditorOptions _options;
    private readonly NameSuggestionDebouncer? _namer;
    private readonly DispatcherTimer _playheadTimer;
    private readonly Dispatcher _dispatcher;
    private ClipSelection _selection;
    private int _cursorFrame;
    private double _playheadFrame = double.NaN;
    private bool _isPlaying;
    private bool _normalize = true;
    private double? _selectionLufs;
    private int _measureVersion;
    private string _name = "";
    private string _nameHint = "";
    private bool _nameEditedByUser;
    private bool _isSuggesting;
    private bool _isSaving;
    private string _status = "";
    private LibraryFolder _selectedFolder;
    private CancellationTokenSource? _playCts;

    public ClipEditorViewModel(EditableAudio audio, IEditorPreview preview, IClipEncoder encoder, ClipEditorOptions options)
    {
        _preview = preview;
        _options = options;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Session = new ClipEditorSession(audio, encoder, options.Loudness);
        _selection = Session.Selection;
        _cursorFrame = Session.Cursor;
        _name = string.IsNullOrWhiteSpace(audio.SuggestedTitle) ? "" : ClipFileNames.Sanitize(audio.SuggestedTitle);

        Folders = LibraryFolderList.Build(new FileSystemFolderSource(options.LibraryRoot));
        _selectedFolder = LibraryFolderList.Find(Folders, options.InitialFolder);

        if (options.SuggestName is { } suggest)
            _namer = new NameSuggestionDebouncer(suggest, options.SuggestDelay, TimeProvider.System);

        _playheadTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher) { Interval = PlayheadInterval };
        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        _preview.Stopped += OnPreviewStopped;

        MeasureSelection();
        if (_name.Length == 0)
            _ = AutoSuggestNameAsync();
    }

    /// <summary>Raised after each clip is saved.</summary>
    public event Action<SavedClip>? ClipSaved;

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

    public string NormalizeLabel => string.Create(CultureInfo.CurrentCulture, $"Normalize loudness to {_options.Loudness.TargetLufs:0.#} LUFS");

    public string LoudnessText
    {
        get
        {
            if (_selectionLufs is not { } lufs)
                return "Measuring loudness…";
            if (!double.IsFinite(lufs))
                return "Selection is silent";

            var measured = string.Create(CultureInfo.CurrentCulture, $"Selection: {lufs:0.0} LUFS");
            if (!_normalize)
                return measured;

            var gain = LoudnessNormalizer.GainDbFor(lufs, _options.Loudness);
            return string.Create(CultureInfo.CurrentCulture, $"{measured}  ({gain:+0.0;−0.0;0.0} dB)");
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

    /// <summary>
    /// A subtle note next to the name box for errors from automatic (debounced) suggestions, e.g. "No speech was
    /// detected" — these mustn't steal the status line from a "Saved …" message or a manual save in progress.
    /// </summary>
    public string NameHint
    {
        get => _nameHint;
        private set
        {
            if (SetField(ref _nameHint, value))
                OnPropertyChanged(nameof(HasNameHint));
        }
    }

    public bool HasNameHint => _nameHint.Length > 0;

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

    public IReadOnlyList<LibraryFolder> Folders { get; }

    public LibraryFolder SelectedFolder
    {
        get => _selectedFolder;
        set => SetField(ref _selectedFolder, value ?? Folders[0]);
    }

    public bool CanSave => Session.CanSave && !_isSaving;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ObservableCollection<SavedClip> SavedClips { get; } = [];

    public ClipEditorOutcome Outcome { get; set; } = ClipEditorOutcome.Done;

    /// <summary>Called when a selection drag ends: snaps its edges to quiet points, re-measures, and re-suggests a name.</summary>
    public void CommitSelection()
    {
        ApplySelection(Session.Select(_selection, snap: true));
        MeasureSelection();
        _ = AutoSuggestNameAsync();
    }

    public void SelectAll()
    {
        Session.SelectAll();
        ApplySelection(Session.Selection);
        MeasureSelection();
        _ = AutoSuggestNameAsync();
    }

    public Task TogglePlayAsync()
    {
        if (!IsPlaying && _playCts is null)
            return PlayAsync(selectionOnly: true);

        Stop();
        return Task.CompletedTask;
    }

    public Task PlaySelectionAsync() => PlayAsync(selectionOnly: true);

    public Task PlayFromCursorAsync() => PlayAsync(selectionOnly: false);

    /// <summary>Stops this editor's preview; another editor window's preview on the shared output is left playing.</summary>
    public void Stop()
    {
        _playCts?.Cancel();
        if (OwnsPreview)
            _preview.Stop();

        PlaybackEnded();
    }

    public async Task SuggestNameAsync()
    {
        if (_namer is null)
            return;

        IsSuggesting = true;
        NameHint = "";
        ApplySuggestion(await _namer.SuggestNowAsync(Session.SelectionAudio()), force: true);
    }

    public async Task SaveAsync()
    {
        if (!CanSave)
            return;

        SetSaving(true);
        var selection = _selection;
        var name = _name;
        var folder = FolderPath(_selectedFolder);
        var normalize = _normalize;
        Status = "Saving…";
        try
        {
            var saved = await Task.Run(() => Session.Save(selection, name, folder, normalize, CancellationToken.None));
            SavedClips.Add(saved);
            Status = saved.Warning is { } warning
                ? $"Saved “{saved.FileName}” to {_selectedFolder.Display}. {warning}"
                : $"Saved “{saved.FileName}” to {_selectedFolder.Display}. Select another bite, or click Done.";
            _namer?.Cancel();
            IsSuggesting = false;
            _name = "";
            _nameEditedByUser = false;
            NameHint = "";
            OnPropertyChanged(nameof(Name));
            ClipSaved?.Invoke(saved);
        }
        catch (Exception e)
        {
            Status = $"Couldn't save: {e.Message}";
        }
        finally
        {
            SetSaving(false);
        }
    }

    /// <summary>Stops preview and any pending name suggestion; call when the editor closes.</summary>
    public void Dispose()
    {
        _preview.Stopped -= OnPreviewStopped;
        Stop();
        _namer?.Dispose();
    }

    private async Task PlayAsync(bool selectionOnly)
    {
        Stop();
        var range = Session.PreviewRange(selectionOnly);
        if (range.Length <= 0)
            return;

        var cts = new CancellationTokenSource();
        _playCts = cts;
        var selection = Session.Selection;
        var lufs = _selectionLufs ?? await Task.Run(() => Session.MeasureSelection(selection));
        try
        {
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

    private bool OwnsPreview => _preview.PlayingAudio == Session.Audio;

    private void UpdatePlayhead()
    {
        if (OwnsPreview && _preview.PositionFrame is { } frame)
            PlayheadFrame = frame;
        else
            PlaybackEnded();
    }

    private void OnPreviewStopped(Exception? error) => _dispatcher.BeginInvoke(() =>
    {
        PlaybackEnded();
        if (error is not null)
            Status = $"Preview stopped: {error.Message}";
    });

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

    private async void MeasureSelection()
    {
        var version = ++_measureVersion;
        var selection = _selection;
        _selectionLufs = null;
        OnPropertyChanged(nameof(LoudnessText));
        var lufs = await Task.Run(() => Session.MeasureSelection(selection));
        if (version != _measureVersion)
            return;

        _selectionLufs = lufs;
        OnPropertyChanged(nameof(LoudnessText));
    }

    private async Task AutoSuggestNameAsync()
    {
        if (_namer is null || _nameEditedByUser || Session.Selection.Length <= 0)
            return;

        IsSuggesting = true;
        ApplySuggestion(await _namer.RequestAsync(Session.SelectionAudio()), force: false);
    }

    // A null result means it was superseded by a newer request, which owns the busy state.
    // Automatic (debounced) suggestions never touch Status: that would overwrite a "Saved …" message with something
    // like "No speech was detected" every time the selection settles. Their errors go to the quieter NameHint instead.
    private void ApplySuggestion(NameSuggestion? suggestion, bool force)
    {
        IsSuggesting = _namer?.IsBusy == true;
        if (suggestion is null)
            return;

        if (suggestion.Error is { } error)
        {
            if (force)
                Status = $"Couldn't suggest a name: {error}";
            else
                NameHint = error;
        }
        else if (suggestion.Name is null)
        {
            if (force)
                Status = "No name suggested.";
        }
        else if (force || !_nameEditedByUser)
        {
            _name = suggestion.Name;
            _nameEditedByUser = false;
            NameHint = "";
            OnPropertyChanged(nameof(Name));
        }
    }

    private void SetSaving(bool saving)
    {
        _isSaving = saving;
        OnPropertyChanged(nameof(CanSave));
    }

    private string FolderPath(LibraryFolder folder) => folder.RelativePath.Length == 0
        ? _options.LibraryRoot
        : Path.Combine(_options.LibraryRoot, folder.RelativePath.Replace('/', Path.DirectorySeparatorChar));
}
