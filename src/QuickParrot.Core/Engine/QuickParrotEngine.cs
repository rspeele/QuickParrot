using System.Collections.Concurrent;
using QuickParrot.Core.Favorites;
using QuickParrot.Core.Grabs;
using QuickParrot.Core.Library;
using QuickParrot.Core.Navigation;
using QuickParrot.Core.Playback;
using QuickParrot.Core.Replay;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Engine;

/// <summary>
/// Owns the chord navigator and playback controller and runs them on one dedicated worker thread.
/// Every public method just queues work and returns, so callers (like a low-level keyboard hook) never block.
/// </summary>
public sealed class QuickParrotEngine : IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly IClipPlayer _player;
    private readonly IMicMuter _micMuter;
    private readonly ISettingsStore _settingsStore;
    private readonly TimeProvider _time;
    private readonly Func<string, IFolderSource> _createLibrary;
    private readonly PlaybackController _controller;
    private readonly IReplaySource? _replay;
    private readonly ReplayGrabber? _grabber;

    private volatile AppSettings _settings;
    private volatile ChordNavigator? _navigator;
    private IFolderSource? _library;
    private ITimer? _saveTimer;
    private OverlayViewState? _publishedViewState;
    private string? _lastPlayed;
    private bool _started;
    private bool _suppressPlayback;

    public QuickParrotEngine(
        IClipPlayer player,
        IPushToTalk pushToTalk,
        IMicMuter micMuter,
        ISettingsStore settingsStore,
        AppSettings settings,
        TimeProvider time,
        Func<string, IFolderSource> createLibrary,
        IReplaySource? replay = null,
        IPendingGrabStore? grabs = null)
    {
        _player = player;
        _micMuter = micMuter;
        _settingsStore = settingsStore;
        _time = time;
        _createLibrary = createLibrary;
        _settings = settings;
        _replay = replay;
        _grabber = replay is null || grabs is null ? null : new ReplayGrabber(replay, grabs);
        _controller = new PlaybackController(player, pushToTalk, micMuter, time, settings.ToPlaybackOptions(), Post);
        _controller.PlaybackFailed += e => ErrorOccurred?.Invoke($"Couldn't play {Path.GetFileName(e.ClipPath)}: {e.Message}");
        _thread = new Thread(RunQueued) { IsBackground = true, Name = "QuickParrot engine" };
    }

    /// <summary>A user-facing error message. Raised on the worker thread.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Raised on the worker thread after any settings change, including navigator persistence.</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>Raised on the worker thread whenever <see cref="ViewState"/> changes (null = hide the overlay).</summary>
    public event Action<OverlayViewState?>? ViewStateChanged;

    /// <summary>Raised on the worker thread once a grab is saved; see <see cref="ReplayGrabber.SavedMessage"/>.</summary>
    public event Action<PendingGrab>? GrabSaved;

    /// <summary>
    /// Raised on the worker thread with a user-facing reason a grab failed; <see cref="ErrorOccurred"/> follows.
    /// </summary>
    public event Action<string>? GrabFailed;

    /// <summary>Raised on the worker thread when a favorite is played, assigned or cleared, or that fails.</summary>
    public event Action<FavoriteNotice>? FavoritesNotice;

    public AppSettings Settings => _settings;

    /// <summary>What the overlay should draw right now; safe to read from any thread.</summary>
    internal OverlayViewState? ViewState => _navigator?.ViewState;

    public void Start()
    {
        _started = true;
        Post(() => ApplySettings(null, _settings));
        _thread.Start();
    }

    public void Post(ChordEvent chordEvent) => Post(() => HandleChordEvent(chordEvent));

    public void Play(string relativePath) => Post(() => PlayRelative(relativePath));

    public void Stop() => Post(_controller.Stop);

    public void PlayFavorite(int slot) => Post(() => PlayFavoriteSlot(slot));

    /// <summary>Puts a library clip on F<paramref name="slot"/>; folders and paths outside the library are refused.</summary>
    public void AssignFavorite(int slot, string relativePath) => Post(() => Assign(slot, relativePath));

    public void ClearFavorite(int slot) => Post(() => Clear(slot));

    /// <summary>Saves the replay buffer as a pending grab, like chord+Enter.</summary>
    public void Grab() => Post(StartGrab);

    /// <summary>While suppressed, Play and chord PlayClip requests are ignored; chord navigation itself still works.
    /// Used by the loopback test so it doesn't record a clip playing over itself.</summary>
    public void SuppressPlayback(bool suppress) => Post(() => _suppressPlayback = suppress);

    /// <summary>Call when capture devices change, e.g. a mic that couldn't be restored is plugged back in.</summary>
    public void RetryMicRestore() => Post(_micMuter.RetryRestore);

    public void UpdateSettings(Func<AppSettings, AppSettings> change) => Post(() =>
    {
        var old = _settings;
        var updated = change(old).Sanitized();
        if (updated.LibraryRoot != old.LibraryRoot && updated.NavigatorPersistentPath == old.NavigatorPersistentPath)
            updated = updated with { NavigatorPersistentPath = "" };

        ApplySettings(old, updated);
        CommitSettings(updated);
    });

    /// <summary>
    /// Completes once everything queued before this call has run. Work those items queue in turn (like a
    /// finished prepare) may still be pending.
    /// </summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(() => done.SetResult()))
            done.SetResult();

        return done.Task;
    }

    /// <summary>Stops playback (releasing push-to-talk), saves pending settings and stops the worker thread.</summary>
    public void Dispose()
    {
        if (_queue.IsAddingCompleted)
            return;

        TryPost(Shutdown);
        _queue.CompleteAdding();
        if (_started)
            _thread.Join(TimeSpan.FromSeconds(5));
        else
            RunQueued();
    }

    private void Post(Action action) => TryPost(action);

    private bool TryPost(Action action)
    {
        try
        {
            _queue.Add(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // shutting down
        }
    }

    private void RunQueued()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ErrorOccurred?.Invoke($"Unexpected error: {e.Message}");
            }

            PublishViewState();
        }
    }

    private void PublishViewState()
    {
        var current = ViewState;
        if (ReferenceEquals(current, _publishedViewState))
            return;

        _publishedViewState = current;
        try
        {
            ViewStateChanged?.Invoke(current);
        }
        catch (Exception e)
        {
            ErrorOccurred?.Invoke($"Unexpected error: {e.Message}");
        }
    }

    private void HandleChordEvent(ChordEvent chordEvent)
    {
        if (_navigator is not { } navigator)
        {
            if (chordEvent is GrabPressed)
                StartGrab(); // grabbing doesn't need a library
            else if (chordEvent is FavoritePressed or ChordlessFavoritePressed)
                Notify(FavoriteNotice.NoLibrary);
            return;
        }

        foreach (var action in navigator.Handle(chordEvent))
        {
            switch (action)
            {
                case PlayClip play:
                    PlayRelative(play.RelativePath);
                    break;
                case StopPlayback:
                    _controller.Stop();
                    break;
                case GrabReplay:
                    StartGrab();
                    break;
                case PlayFavorite favorite:
                    PlayFavoriteSlot(favorite.Slot);
                    break;
                case AssignFavorite assign:
                    Assign(assign.Slot, assign.RelativePath);
                    break;
                case AssignLastPlayedFavorite assign:
                    AssignLastPlayed(assign.Slot);
                    break;
                case ClearFavorite clear:
                    Clear(clear.Slot);
                    break;
            }
        }
    }

    private void PlayRelative(string relativePath)
    {
        if (_suppressPlayback)
            return;

        var fullPath = _library?.GetFullPath(relativePath);
        if (fullPath is null)
        {
            ErrorOccurred?.Invoke($"Couldn't play {relativePath}: it isn't inside the sound library.");
            return;
        }

        _lastPlayed = relativePath;
        _controller.Play(fullPath);
    }

    private void PlayFavoriteSlot(int slot)
    {
        if (!FavoriteSlots.IsValidSlot(slot))
            return;

        if (_settings.Favorites[slot] is not { } path)
            Notify(FavoriteNotice.SlotEmpty(slot));
        else if (_library is null)
            Notify(FavoriteNotice.NoLibrary);
        else if (!_library.ClipExists(path))
            Notify(FavoriteNotice.SlotMissing(slot));
        else
            PlayRelative(path);
    }

    private void Assign(int slot, string relativePath)
    {
        if (!FavoriteSlots.IsValidSlot(slot))
            return;

        if (_library is null)
        {
            Notify(FavoriteNotice.NoLibrary);
            return;
        }

        var path = FavoriteSlots.NormalizePath(relativePath);
        if (path is null || !_library.ClipExists(path))
        {
            Notify(FavoriteNotice.NotAClip);
            return;
        }

        UpdateFavorites(_settings.Favorites.With(slot, path));
        Notify(FavoriteNotice.Assigned(slot, path));
    }

    private void AssignLastPlayed(int slot)
    {
        if (_lastPlayed is not { } path)
            Notify(FavoriteNotice.NothingPlayed);
        else if (_library?.ClipExists(path) != true)
            Notify(FavoriteNotice.LastPlayedMissing);
        else
            Assign(slot, path);
    }

    private void Clear(int slot)
    {
        if (!FavoriteSlots.IsValidSlot(slot))
            return;

        if (_settings.Favorites[slot] is null)
        {
            Notify(FavoriteNotice.AlreadyEmpty(slot));
            return;
        }

        UpdateFavorites(_settings.Favorites.With(slot, null));
        Notify(FavoriteNotice.Cleared(slot));
    }

    private void UpdateFavorites(FavoriteSlots favorites)
    {
        CommitSettings(_settings with { Favorites = favorites });
        _navigator?.RefreshFavorites();
    }

    // Runs on the worker thread when assign mode starts, so the file checks stay off the keyboard hook.
    private FavoritesPanel BuildFavoritesPanel(int targetSlot)
    {
        var settings = _settings;
        var library = _library;
        var slots = FavoriteStatus.Describe(
            settings.Favorites, library is null ? null : library.ClipExists, settings.ChordKey, settings.PushToTalkBinding);
        var lastPlayed = _lastPlayed is { } path && library?.ClipExists(path) == true ? FavoriteSlots.DisplayName(path) : null;
        return new FavoritesPanel(slots, targetSlot, lastPlayed, settings.ChordKey.ToString());
    }

    private void Notify(FavoriteNotice notice) => FavoritesNotice?.Invoke(notice);

    // Snapshotting and saving run on the thread pool, so disk IO never delays chord handling.
    private void StartGrab()
    {
        if (_grabber is not { } grabber || !_settings.ReplayBufferEnabled)
        {
            ReportGrabFailure(ReplayGrabber.DisabledMessage);
            return;
        }

        var length = TimeSpan.FromSeconds(_settings.ReplayBufferSeconds);
        var grabbedAt = _time.GetLocalNow();
        _ = Task.Run(() =>
        {
            var result = grabber.Grab(length, grabbedAt);
            Post(() =>
            {
                if (result.Grab is { } grab)
                    GrabSaved?.Invoke(grab);
                else
                    ReportGrabFailure(result.Error ?? "The grab failed.");
            });
        });
    }

    private void ReportGrabFailure(string message)
    {
        GrabFailed?.Invoke(message);
        ErrorOccurred?.Invoke(message);
    }

    private void ApplySettings(AppSettings? old, AppSettings updated)
    {
        if (_replay is not null)
            _replay.Capacity = TimeSpan.FromSeconds(updated.ReplayBufferSeconds);

        _controller.Options = updated.ToPlaybackOptions();
        _player.Configure(updated.ToOutputSettings());
        _micMuter.Configure(updated.ToMicDuckSettings());

        if (old is null || old.LibraryRoot != updated.LibraryRoot)
            OpenLibrary(updated);
    }

    private void OpenLibrary(AppSettings settings)
    {
        if (_navigator is not null)
            _navigator.PersistentPathChanged -= OnPersistentPathChanged;

        _lastPlayed = null; // relative to the old library
        if (string.IsNullOrEmpty(settings.LibraryRoot))
        {
            _library = null;
            _navigator = null;
            return;
        }

        _library = _createLibrary(settings.LibraryRoot);
        var navigator = new ChordNavigator(_library, settings.NavigatorPersistentPath, BuildFavoritesPanel);
        navigator.PersistentPathChanged += OnPersistentPathChanged;
        _navigator = navigator;
    }

    private void OnPersistentPathChanged(string path) =>
        CommitSettings(_settings with { NavigatorPersistentPath = path });

    // Saves are throttled (one write per SaveDelay window, not reset by later changes) so dragging a volume
    // slider doesn't write the file on every tick.
    private void CommitSettings(AppSettings updated)
    {
        _settings = updated;
        _saveTimer ??= _time.CreateTimer(_ => Post(SaveNow), null, SaveDelay, Timeout.InfiniteTimeSpan);
        SettingsChanged?.Invoke(updated);
    }

    private void SaveNow()
    {
        if (_saveTimer is null)
            return;

        _saveTimer.Dispose();
        _saveTimer = null;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorOccurred?.Invoke($"Couldn't save settings: {e.Message}");
        }
    }

    private void Shutdown()
    {
        try
        {
            _controller.Dispose();
        }
        finally
        {
            SaveNow();
        }
    }
}
