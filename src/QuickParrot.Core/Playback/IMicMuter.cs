using QuickParrot.Core.Mic;

namespace QuickParrot.Core.Playback;

/// <summary>Silences the user's real microphone while a clip plays. Implementations must not throw.</summary>
public interface IMicMuter
{
    /// <summary>Takes effect from the next <see cref="Mute"/>; a mic already muted is still restored.</summary>
    void Configure(MicDuckSettings settings);

    void Mute();

    void Unmute();
}
