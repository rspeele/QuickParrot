namespace QuickParrot.Core.Diagnostics;

/// <summary>Wording shared by the Diagnostics tests that record the cable.</summary>
public static class CableTestMessages
{
    public const string NoMonitor = "no headphones or speakers are selected apart from the cable.";

    public const string NoAudioFromCable =
        "CABLE Output didn't deliver any audio. Check that it's enabled and not in use by another app in exclusive mode.";
}
