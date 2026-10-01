using QuickParrot.Core.Mic;

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
    private readonly Action<PlaybackError> _failed;
    private readonly Action<Action> _dispatch;

    private ITimer? _timer;
    private long _timerGeneration;
    private PrepareRequest? _request;
    private IPreparedClip? _readyClip;
    private bool _preRollElapsed;
    private string _currentPath = "";
    private long _playId;
    private bool _pushToTalkHeld;
    private bool _micMuted;

    /// <param name="failed">Called when a clip can't be loaded, can't start, or fails mid-play.</param>
    /// <param name="dispatch">Routes timer, prepare and player callbacks back onto the controller's thread.
    /// Defaults to running them inline.</param>
    public PlaybackController(
        IClipPlayer player,
        IPushToTalk pushToTalk,
        IMicMuter micMuter,
        TimeProvider time,
        PlaybackOptions options,
        Action<PlaybackError> failed,
        Action<Action>? dispatch = null)
    {
        _player = player;
        _pushToTalk = pushToTalk;
        _micMuter = micMuter;
        _time = time;
        _failed = failed;
        _dispatch = dispatch ?? (action => action());
        Options = options;
        _player.Finished += OnPlayerFinished;
    }

    /// <summary>Takes effect from the next play request; a key already pressed is still released.</summary>
    public PlaybackOptions Options { get; set; }

    internal PlaybackPhase Phase { get; private set; }

    /// <summary>
    /// Starts preparing <paramref name="fullPath"/>. From idle it plays once both the pre-roll and the prepare are
    /// done; otherwise it replaces whatever is pending or playing as soon as it's prepared.
    /// </summary>
    public void Play(string fullPath)
    {
        Engage();
        CancelPending();

        if (Phase == PlaybackPhase.Idle)
        {
            Phase = PlaybackPhase.PreRoll;
            _preRollElapsed = Options.PreRoll <= TimeSpan.Zero;
            if (!_preRollElapsed)
                StartTimer(Options.PreRoll);
        }
        else if (Phase == PlaybackPhase.PostRoll)
        {
            CancelTimer(); // keep the key held for the replacement
        }

        BeginPrepare(fullPath);
    }

    /// <summary>Cuts playback off and releases immediately; no post-roll is needed after an abrupt stop.</summary>
    public void Stop()
    {
        if (Phase != PlaybackPhase.Idle)
            Reset();
    }

    public void Dispose()
    {
        try
        {
            Stop();
        }
        finally
        {
            CancelTimer();
            _player.Finished -= OnPlayerFinished;
        }
    }

    private void BeginPrepare(string fullPath)
    {
        var request = new PrepareRequest(fullPath);
        _request = request;

        Task<IPreparedClip> task;
        try
        {
            task = _player.PrepareAsync(fullPath, request.Token);
        }
        catch (Exception e)
        {
            task = Task.FromException<IPreparedClip>(e);
        }

        // Attached last: with inline dispatch and an already-finished task, this runs OnPrepared right away.
        task.ContinueWith(
            completed =>
            {
                if (request.TryComplete(completed))
                    _dispatch(() => OnPrepared(request));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnPrepared(PrepareRequest request)
    {
        if (request != _request || request.Take() is not { } result)
            return; // superseded: whoever cancelled it disposes the clip

        _request = null;
        if (!result.IsCompletedSuccessfully)
        {
            Fail(request.FullPath, Unwrap(result));
            return;
        }

        var clip = result.Result;
        if (Phase == PlaybackPhase.PreRoll && !_preRollElapsed)
        {
            _readyClip = clip;
        }
        else
        {
            CancelTimer();
            StartClip(clip);
        }
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
        if (_request is not null)
            return; // a replacement is being prepared; hold the key until it starts

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
        {
            _preRollElapsed = true;
            if (_readyClip is { } clip)
            {
                _readyClip = null;
                StartClip(clip);
            }
        }
        else if (Phase == PlaybackPhase.PostRoll)
        {
            Reset();
        }
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
        try
        {
            Reset();
        }
        finally
        {
            _failed(new PlaybackError(clipPath, Describe(error)));
        }
    }

    // The library can change after the overlay was drawn, so a vanished file is an expected failure.
    private static string Describe(Exception error) =>
        error is FileNotFoundException or DirectoryNotFoundException ? "The file no longer exists." : error.Message;

    private static Exception Unwrap(Task task) =>
        task.Exception?.InnerException ?? new OperationCanceledException("Preparing the clip was cancelled.");

    // Releasing the key and unmuting must happen even if stopping the audio throws.
    private void Reset()
    {
        var wasPlaying = Phase is PlaybackPhase.Playing or PlaybackPhase.PostRoll;
        try
        {
            CancelTimer();
            CancelPending();
            if (wasPlaying)
                _player.Stop();
        }
        finally
        {
            Phase = PlaybackPhase.Idle;
            Disengage();
        }
    }

    private void CancelPending()
    {
        var request = _request;
        var readyClip = _readyClip;
        _request = null;
        _readyClip = null;
        try
        {
            request?.Cancel();
        }
        finally
        {
            readyClip?.Dispose();
        }
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
        try
        {
            if (_pushToTalkHeld)
            {
                _pushToTalkHeld = false;
                _pushToTalk.Release();
            }
        }
        finally
        {
            if (_micMuted)
            {
                _micMuted = false;
                _micMuter.Unmute();
            }
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
