using QuickParrot.App.Mvvm;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Library;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

/// <summary>The Diagnostics tab's "hear a clip as others hear it" test; the sequencing lives in <see cref="ClipCableTest"/>.</summary>
public sealed class ClipCableTestViewModel : ObservableObject
{
    public const string Note =
        "Plays the chosen clip for real, exactly like a hotkey (others in voice chat will hear it; push-to-talk and mic "
        + "muting happen as usual), while recording CABLE Output, then plays that recording back to you. It's what "
        + "reaches the cable, before your voice app's own processing such as noise suppression or auto gain. "
        + "Recordings stop after 30 s.";

    private const int MaxClips = 5000;

    private readonly ClipCableTest _test;
    private readonly CableTestRunner _runner;
    private readonly QuickParrotEngine _engine;
    private readonly SettingsMirror _settings;
    private readonly Func<string, IFolderSource> _openSource;
    private bool _clipsStale = true;
    private int _listGeneration;
    private IReadOnlyList<string> _clips = [];
    private string? _selectedClip;
    private bool _isRunning;
    private bool _isReplaying;
    private string _progress = "";
    private string _resultText = "";
    private MonoAudio? _lastRecording;

    public ClipCableTestViewModel(
        ClipCableTest test,
        CableTestRunner runner,
        QuickParrotEngine engine,
        SettingsMirror settings,
        Func<string, IFolderSource> openSource)
    {
        _test = test;
        _runner = runner;
        _engine = engine;
        _settings = settings;
        _openSource = openSource;
        _runner.Changed += OnStateChanged;
        _settings.Changed += (old, now) =>
        {
            if (old.LibraryRoot == now.LibraryRoot)
                return;

            _listGeneration++;
            Clips = [];
            SelectedClip = null;
            MarkClipsStale();
        };
    }

    public IReadOnlyList<string> Clips
    {
        get => _clips;
        private set => SetField(ref _clips, value);
    }

    public string? SelectedClip
    {
        get => _selectedClip;
        set
        {
            if (SetField(ref _selectedClip, value))
                OnPropertyChanged(nameof(CanRun));
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
                OnStateChanged();
        }
    }

    public bool IsReplaying
    {
        get => _isReplaying;
        private set
        {
            if (SetField(ref _isReplaying, value))
                OnStateChanged();
        }
    }

    public bool IsIdle => !IsRunning && !IsReplaying;

    public bool CanRun => IsRunning || (_runner.IsSetupReady && SelectedClip is not null && !_runner.IsBusy);

    public string ButtonLabel => IsRunning ? "Cancel" : "Play and record";

    public bool CanReplay => IsReplaying || (_lastRecording is not null && !_runner.IsBusy);

    public string ReplayLabel => IsReplaying ? "Stop" : "Hear it again";

    public string Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set
        {
            if (SetField(ref _resultText, value))
                OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => _resultText.Length > 0;

    /// <summary>The library's files changed, so the list is re-read the next time the picker opens.</summary>
    public void MarkClipsStale() => _clipsStale = true;

    /// <summary>Lists the library's clips off the UI thread if they may have changed, keeping the selection if it
    /// still exists. Called when the picker opens, so the library isn't walked until someone wants the list.</summary>
    public async Task LoadClipsIfStaleAsync()
    {
        if (!_clipsStale)
            return;

        _clipsStale = false;
        var generation = ++_listGeneration;
        var root = _settings.Current.LibraryRoot;
        IReadOnlyList<string> clips;
        try
        {
            clips = string.IsNullOrEmpty(root) ? [] : await Task.Run(() => LibraryClips.List(_openSource(root), MaxClips));
        }
        catch (Exception e)
        {
            Progress = $"Couldn't list the library's clips: {e.Message}";
            _clipsStale = true;
            return;
        }

        if (generation != _listGeneration)
            return;

        var selected = SelectedClip;
        Clips = clips;
        SelectedClip = selected is not null && clips.Contains(selected) ? selected : null;
    }

    /// <summary>Starts the test, or cancels one already running (keeping its result if only the playback is cut).</summary>
    public async Task RunOrCancelAsync()
    {
        if (IsRunning)
        {
            _runner.Cancel();
            return;
        }

        if (SelectedClip is not { } clip || _runner.TryStart(recordsCable: true) is not { } run)
            return;

        using (run)
        {
            _lastRecording = null;
            ResultText = "";
            Progress = "";
            IsRunning = true;
            try
            {
                if (BuildRequest(clip, run.Setup) is { } request)
                    await RecordAndPlayBackAsync(run, request, clip);
                else
                    ResultText = "That clip is no longer in the sound library.";
            }
            catch (Exception e)
            {
                ResultText = $"The test failed: {e.Message}";
                Progress = "";
            }
            finally
            {
                IsRunning = false;
            }
        }
    }

    /// <summary>Plays the last recording again on the monitor device, or stops a replay in progress.</summary>
    public async Task ReplayOrStopAsync()
    {
        if (IsReplaying)
        {
            _runner.Cancel();
            return;
        }

        if (_lastRecording is not { } recording || _runner.TryStart(recordsCable: false) is not { } run)
            return;

        using (run)
        {
            IsReplaying = true;
            Progress = "";
            try
            {
                if (await _test.PlayBackAsync(run.Setup.MonitorRenderId, recording, run.Token) is { } error)
                    Progress = $"Couldn't play the recording back: {error}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Progress = $"Couldn't play the recording back: {e.Message}";
            }
            finally
            {
                IsReplaying = false;
            }
        }
    }

    private async Task RecordAndPlayBackAsync(CableTestRunner.Run run, ClipCableTestRequest request, string clip)
    {
        var progress = new Progress<string>(text => Progress = text); // must be built on the UI thread to post back here
        ClipCableTestResult result;
        try
        {
            result = await _test.RunAsync(request, () => _engine.PlayDespiteSuppression(clip), _engine.Stop, progress, run.Token);
        }
        catch (OperationCanceledException)
        {
            Progress = "Test cancelled";
            return;
        }

        _lastRecording = result.Recording;
        ResultText = string.Join(Environment.NewLine, result.Describe());
        Progress = "";
        if (result.Recording is not { } recording)
            return;

        // Cancelling from here on only stops the playback; the result above stands.
        Progress = "Playing back what others heard…";
        try
        {
            if (await _test.PlayBackAsync(run.Setup.MonitorRenderId, recording, run.Token) is { } error)
                ResultText = string.Join(Environment.NewLine, (result with { PlaybackError = error }).Describe());
            Progress = "";
        }
        catch (OperationCanceledException)
        {
            Progress = "Playback stopped";
        }
    }

    private ClipCableTestRequest? BuildRequest(string clip, LoopbackTestSetup setup)
    {
        var settings = _settings.Current;
        if (string.IsNullOrEmpty(settings.LibraryRoot) || _openSource(settings.LibraryRoot).GetFullPath(clip) is not { } fullPath)
            return null;

        var options = settings.ToPlaybackOptions();
        return new ClipCableTestRequest(
            FavoriteSlots.DisplayName(clip), fullPath, setup.CableCaptureId!, options.PreRoll, options.PostRoll);
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ButtonLabel));
        OnPropertyChanged(nameof(CanReplay));
        OnPropertyChanged(nameof(ReplayLabel));
    }
}
