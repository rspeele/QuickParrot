namespace QuickParrot.Core.Replay;

/// <summary>The rolling "instant replay" audio, as seen by the engine. Thread-safe.</summary>
public interface IReplaySource
{
    /// <summary>How much audio is kept. Changing it keeps the most recent audio that still fits.</summary>
    TimeSpan Capacity { get; set; }

    /// <summary>The most recent audio, at most <paramref name="duration"/> and <see cref="Capacity"/> long.</summary>
    ReplaySnapshot Snapshot(TimeSpan duration);
}
