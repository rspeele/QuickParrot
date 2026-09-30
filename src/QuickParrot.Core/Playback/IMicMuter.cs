using QuickParrot.Core.Mic;

namespace QuickParrot.Core.Playback;

/// <summary>Silences the user's real microphone while a clip plays. Implementations must not throw.</summary>
public interface IMicMuter
{
    /// <summary>Takes effect from the next <see cref="Mute"/>; a mic already muted is still restored.</summary>
    void Configure(MicDuckSettings settings);

    void Mute();

    void Unmute();

    /// <summary>Retries undoing a change that couldn't be undone earlier, if any, without touching a clip's own.</summary>
    void RetryRestore();
}
