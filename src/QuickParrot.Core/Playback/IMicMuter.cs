namespace QuickParrot.Core.Playback;

/// <summary>Silences the user's real microphone while a clip plays. Implementations must not throw.</summary>
public interface IMicMuter
{
    void Mute();

    void Unmute();
}
