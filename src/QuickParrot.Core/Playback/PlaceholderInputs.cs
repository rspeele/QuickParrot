using System.Diagnostics;

namespace QuickParrot.Core.Playback;

// Stand-ins until the real SendInput push-to-talk and microphone muting exist.
public sealed class LoggingPushToTalk : IPushToTalk
{
    public void Press() => Debug.WriteLine("QuickParrot: push-to-talk press");

    public void Release() => Debug.WriteLine("QuickParrot: push-to-talk release");
}

public sealed class LoggingMicMuter : IMicMuter
{
    public void Mute() => Debug.WriteLine("QuickParrot: mic mute");

    public void Unmute() => Debug.WriteLine("QuickParrot: mic unmute");
}
