namespace QuickParrot.Core.Playback;

/// <summary>Holds the game's push-to-talk key while a clip plays. Implementations must not throw.</summary>
public interface IPushToTalk
{
    void Press();

    /// <summary>The implementation, not the caller, must avoid releasing a key the user is physically holding.</summary>
    void Release();
}
