using System.Text.Json.Serialization;

namespace QuickParrot.Core.Keyboard;

/// <summary>Mouse buttons usable for push-to-talk; left and right would break normal clicking.</summary>
public enum PushToTalkMouseButton
{
    Middle = 1,
    X1 = 2,
    X2 = 3,
}

/// <summary>The game's push-to-talk input: exactly one of a physical key or a mouse button.</summary>
public readonly record struct PushToTalkBinding(ScanKey? Key, PushToTalkMouseButton? MouseButton)
{
    public static readonly PushToTalkBinding Default = FromKey(new ScanKey(0x2F, false)); // V

    public static PushToTalkBinding FromKey(ScanKey key) => new(key, null);

    public static PushToTalkBinding FromMouse(PushToTalkMouseButton button) => new(null, button);

    /// <summary>Either a usable key or a middle/side mouse button, regardless of the chord key.</summary>
    [JsonIgnore]
    public bool IsWellFormed => Validate(null) is null;

    /// <summary>Null if this can be push-to-talk alongside <paramref name="chordKey"/>, else why it can't.</summary>
    public string? Validate(ScanKey? chordKey)
    {
        if (Key is null == MouseButton is null)
            return "Pick one key or mouse button for push-to-talk.";

        if (MouseButton is { } button)
            return Enum.IsDefined(button) ? null : "Only the middle and side mouse buttons can be push-to-talk.";

        var key = Key!.Value;
        if (key == chordKey)
            return "Push-to-talk can't be the chord key.";

        if (key.Digit >= 0)
            return "Number keys are used by the chord, so they can't be push-to-talk.";

        if (key.IsShift)
            return "Shift is used by the chord, so it can't be push-to-talk.";

        if (key.IsEnter)
            return "Enter grabs the replay with the chord, so it can't be push-to-talk.";

        // Escape cancels key capture, and a simulated Windows key would open the Start menu.
        if (key.ScanCode is <= 0 or > 0xFF || key == ScanKey.Escape || key.IsWindowsKey)
            return $"{key} can't be push-to-talk.";

        return null;
    }

    /// <summary>Null if <paramref name="chordKey"/> can become the chord key alongside this binding, else why not.</summary>
    public string? ValidateChordKey(ScanKey chordKey)
    {
        if (!chordKey.IsValidChordKey)
            return $"{chordKey} can't be the chord key. Pick another key.";

        return Key == chordKey ? $"{chordKey} is the push-to-talk key, so it can't be the chord key." : null;
    }

    public override string ToString() => MouseButton switch
    {
        PushToTalkMouseButton.Middle when Key is null => "Middle Mouse",
        PushToTalkMouseButton.X1 when Key is null => "Mouse 4",
        PushToTalkMouseButton.X2 when Key is null => "Mouse 5",
        null when Key is { } key => key.ToString(),
        _ => "None",
    };
}
