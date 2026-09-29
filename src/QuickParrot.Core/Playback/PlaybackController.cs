namespace QuickParrot.Core.Playback;

/// <summary>
/// Sequences push-to-talk, mic muting and playback: press, pre-roll, play, post-roll, release.
/// Not thread-safe: every call, and every callback routed through the dispatch delegate, must run on one thread.
/// </summary>
public sealed class PlaybackController : IDisposable
{
    private readonly IClipPlayer _player;
    private readonly IPushToTalk _pushToTalk;
    private readonly IMicMuter _micMuter;
    private readonly TimeProvider _time;
    private readonly Action<Action> _dispatch;

    private ITimer? _timer;
    private long _timerGeneration;
    private IPreparedClip? _pendingClip;
    private string _currentPath = "";
    private long _playId;
    private bool _pushToTalkHeld;
    private bool _micMuted;

    /// <param name="dispatch">Routes timer and player callbacks back onto the controller's thread.
    /// Defaults to running them inline.</param>
    public PlaybackController(
        IClipPlayer player,
        IPushToTalk pushToTalk,
        IMicMuter micMuter,
        TimeProvider time,
        PlaybackOptions options,
        Action<Action>? dispatch = null)
    {
        _player = player;
        _pushToTalk = pushToTalk;
        _micMuter = micMuter;
        _time = time;
        _dispatch = dispatch ?? (action => action());
        Options = options;
        _player.Finished += OnPlayerFinished;
    }

    /// <summary>Takes effect from the next play request; a key already pressed is still released.</summary>
    public PlaybackOptions Options { get; set; }

    public PlaybackPhase Phase { get; private set; }

    /// <summary>Raised when a clip can't be loaded, can't start, or fails mid-play.</summary>
    public event Action<PlaybackError>? PlaybackFailed;

    public void Play(string fullPath)
    {
        var requestedAt = _time.GetTimestamp();
        Engage();

        // Preparing (decode + opening devices) overlaps the pre-roll when idle, so it adds no latency.
        IPreparedClip clip;
        try
        {
            clip = _player.Prepare(fullPath);
        }
        catch (Exception e)
        {
            Fail(fullPath, e);
            return;
        }

        switch (Phase)
        {
            case PlaybackPhase.Idle:
                _pendingClip = clip;
                Phase = PlaybackPhase.PreRoll;
                var remaining = Options.PreRoll - _time.GetElapsedTime(requestedAt);
                if (remaining > TimeSpan.Zero)
                    StartTimer(remaining);
                else
                    StartPendingClip();
                break;

            case PlaybackPhase.PreRoll:
                _pendingClip?.Dispose();
                _pendingClip = clip; // the pre-roll timer already running still applies
                break;

            default:
                CancelTimer();
                StartClip(clip);
                break;
        }
    }

    /// <summary>Cuts playback off and releases immediately; no post-roll is needed after an abrupt stop.</summary>
    public void Stop()
    {
        if (Phase != PlaybackPhase.Idle)
            Reset();
    }

    public void Dispose()
    {
        Stop();
        CancelTimer();
        _player.Finished -= OnPlayerFinished;
    }

    private void OnPlayerFinished(ClipFinished finished) => _dispatch(() => HandleFinished(finished));

    private void HandleFinished(ClipFinished finished)
    {
        if (Phase != PlaybackPhase.Playing || finished.PlayId != _playId)
            return; // stale: that clip was already replaced or stopped

        if (finished.Error is not null)
        {
            Fail(_currentPath, finished.Error);
            return;
        }

        Phase = PlaybackPhase.PostRoll;
        if (Options.PostRoll > TimeSpan.Zero)
            StartTimer(Options.PostRoll);
        else
            Reset();
    }

    private void OnTimer(long generation)
    {
        if (generation != _timerGeneration)
            return;

        CancelTimer();
        if (Phase == PlaybackPhase.PreRoll)
            StartPendingClip();
        else if (Phase == PlaybackPhase.PostRoll)
            Reset();
    }

    private void StartPendingClip()
    {
        var clip = _pendingClip!;
        _pendingClip = null;
        StartClip(clip);
    }

    private void StartClip(IPreparedClip clip)
    {
        _playId++;
        _currentPath = clip.FullPath;
        Phase = PlaybackPhase.Playing;
        try
        {
            _player.Play(clip, _playId);
        }
        catch (Exception e)
        {
            Fail(clip.FullPath, e);
        }
    }

    private void Fail(string clipPath, Exception error)
    {
        Reset();
        PlaybackFailed?.Invoke(new PlaybackError(clipPath, Describe(error)));
    }

    // The library can change after the overlay was drawn, so a vanished file is an expected failure.
    private static string Describe(Exception error) =>
        error is FileNotFoundException or DirectoryNotFoundException ? "The file no longer exists." : error.Message;

    private void Reset()
    {
        CancelTimer();
        _pendingClip?.Dispose();
        _pendingClip = null;
        if (Phase != PlaybackPhase.Idle)
            _player.Stop();

        Phase = PlaybackPhase.Idle;
        Disengage();
    }

    private void Engage()
    {
        if (Options.PushToTalkEnabled && !_pushToTalkHeld)
        {
            _pushToTalk.Press();
            _pushToTalkHeld = true;
        }

        if (Options.MicMuteEnabled && !_micMuted)
        {
            _micMuter.Mute();
            _micMuted = true;
        }
    }

    private void Disengage()
    {
        if (_pushToTalkHeld)
        {
            _pushToTalkHeld = false;
            _pushToTalk.Release();
        }

        if (_micMuted)
        {
            _micMuted = false;
            _micMuter.Unmute();
        }
    }

    private void StartTimer(TimeSpan dueTime)
    {
        CancelTimer();
        var generation = _timerGeneration;
        _timer = _time.CreateTimer(_ => _dispatch(() => OnTimer(generation)), null, dueTime, Timeout.InfiniteTimeSpan);
    }

    // Bumping the generation makes any callback already queued by the old timer a no-op.
    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
        _timerGeneration++;
    }
}
