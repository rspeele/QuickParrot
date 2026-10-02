using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests.Keyboard;

public class PushToTalkBindingTests
{
    private static readonly ScanKey V = new(0x2F, false);
    private static readonly ScanKey B = ScanKey.DefaultChordKey;

    [Theory]
    [InlineData(0x2F, false)] // V
    [InlineData(0x3A, false)] // Caps Lock
    [InlineData(0x1D, false)] // Left Ctrl
    [InlineData(0x38, true)] // Right Alt
    [InlineData(0x3B, false)] // F1
    [InlineData(0x47, true)] // Home, not numpad 7
    public void UsableKeys_AreValid(int scanCode, bool isExtended)
    {
        Assert.Null(PushToTalkBinding.FromKey(new ScanKey(scanCode, isExtended)).Validate(B));
    }

    [Theory]
    [InlineData(0x02, false)] // 1
    [InlineData(0x0B, false)] // 0
    [InlineData(0x47, false)] // Numpad 7
    [InlineData(0x2A, false)] // Left Shift
    [InlineData(0x36, false)] // Right Shift
    [InlineData(0x2A, true)] // fake shift
    [InlineData(0x1C, false)] // Enter
    [InlineData(0x1C, true)] // Numpad Enter
    [InlineData(0x4A, false)] // Numpad -, the chord key
    [InlineData(0x01, false)] // Escape
    [InlineData(0x5B, true)] // Left Windows
    [InlineData(0, false)]
    [InlineData(0x100, false)]
    public void ChordKeysDigitsShiftAndOddKeys_AreInvalid(int scanCode, bool isExtended)
    {
        Assert.NotNull(PushToTalkBinding.FromKey(new ScanKey(scanCode, isExtended)).Validate(B));
    }

    [Fact]
    public void ChordKey_IsCheckedAgainstTheGivenChordKey()
    {
        var binding = PushToTalkBinding.FromKey(V);

        Assert.Equal("Push-to-talk can't be the chord key.", binding.Validate(V));
        Assert.Null(binding.Validate(B));
        Assert.True(binding.IsWellFormed);
    }

    [Fact]
    public void ValidateChordKey_RefusesThePushToTalkKeyAndInvalidChordKeys()
    {
        var binding = PushToTalkBinding.FromKey(V);

        Assert.Equal("V is the push-to-talk key, so it can't be the chord key.", binding.ValidateChordKey(V));
        Assert.NotNull(binding.ValidateChordKey(new ScanKey(0x02, false))); // 1
        Assert.Null(binding.ValidateChordKey(B));
        Assert.Null(PushToTalkBinding.FromMouse(PushToTalkMouseButton.X1).ValidateChordKey(V));
    }

    [Theory]
    [InlineData(PushToTalkMouseButton.Middle)]
    [InlineData(PushToTalkMouseButton.X1)]
    [InlineData(PushToTalkMouseButton.X2)]
    public void MiddleAndSideButtons_AreValid(PushToTalkMouseButton button)
    {
        Assert.Null(PushToTalkBinding.FromMouse(button).Validate(B));
    }

    [Theory]
    [InlineData(0)] // no left or right button exists in the enum
    [InlineData(4)]
    [InlineData(-1)]
    public void UndefinedMouseButtons_AreInvalid(int button)
    {
        Assert.NotNull(PushToTalkBinding.FromMouse((PushToTalkMouseButton)button).Validate(B));
    }

    [Fact]
    public void NeitherOrBoth_AreInvalid()
    {
        Assert.False(default(PushToTalkBinding).IsWellFormed);
        Assert.False(new PushToTalkBinding(V, PushToTalkMouseButton.X1).IsWellFormed);
    }

    [Fact]
    public void DisplayNames()
    {
        Assert.Equal("V", PushToTalkBinding.FromKey(V).ToString());
        Assert.Equal("Home", PushToTalkBinding.FromKey(new ScanKey(0x47, true)).ToString());
        Assert.Equal("Middle Mouse", PushToTalkBinding.FromMouse(PushToTalkMouseButton.Middle).ToString());
        Assert.Equal("Mouse 4", PushToTalkBinding.FromMouse(PushToTalkMouseButton.X1).ToString());
        Assert.Equal("Mouse 5", PushToTalkBinding.FromMouse(PushToTalkMouseButton.X2).ToString());
        Assert.Equal("None", default(PushToTalkBinding).ToString());
        Assert.Equal("V", PushToTalkBinding.Default.ToString());
    }

    [Fact]
    public void Settings_DefaultToVDisabled()
    {
        var settings = JsonSettingsStore.Deserialize("{}");

        Assert.Equal(PushToTalkBinding.FromKey(V), settings.PushToTalkBinding);
        Assert.False(settings.PushToTalkEnabled);
    }

    [Theory]
    [InlineData("""{ "key": { "scanCode": 58 } }""", 0x3A, null)]
    [InlineData("""{ "key": { "scanCode": 29, "isExtended": true }, "mouseButton": null }""", 0x1D, null)]
    [InlineData("""{ "mouseButton": 1 }""", null, PushToTalkMouseButton.Middle)]
    [InlineData("""{ "mouseButton": 3 }""", null, PushToTalkMouseButton.X2)]
    public void Settings_ReadKeyAndMouseBindings(string bindingJson, int? scanCode, PushToTalkMouseButton? button)
    {
        var settings = JsonSettingsStore.Deserialize(
            $$"""{ "pushToTalkEnabled": true, "pushToTalkBinding": {{bindingJson}} }""");

        Assert.Equal(scanCode, settings.PushToTalkBinding.Key?.ScanCode);
        Assert.Equal(button, settings.PushToTalkBinding.MouseButton);
        Assert.True(settings.PushToTalkEnabled);
    }

    [Fact]
    public void Settings_RoundTripBindings()
    {
        foreach (var binding in new[]
                 {
                     PushToTalkBinding.FromKey(new ScanKey(0x1D, true)),
                     PushToTalkBinding.FromMouse(PushToTalkMouseButton.X1),
                 })
        {
            var settings = new AppSettings { PushToTalkEnabled = true, PushToTalkBinding = binding };

            Assert.Equal(settings, JsonSettingsStore.Deserialize(JsonSettingsStore.Serialize(settings)));
        }
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "key": { "scanCode": 3 } }""")] // 2
    [InlineData("""{ "key": { "scanCode": 42 } }""")] // left shift
    [InlineData("""{ "key": { "scanCode": 28 } }""")] // Enter
    [InlineData("""{ "key": { "scanCode": 74 } }""")] // Numpad -, the chord key
    [InlineData("""{ "mouseButton": 0 }""")]
    [InlineData("""{ "mouseButton": 9 }""")]
    [InlineData("""{ "key": { "scanCode": 47 }, "mouseButton": 2 }""")]
    public void Settings_InvalidBinding_FallsBackToDefaultAndDisables(string bindingJson)
    {
        var settings = JsonSettingsStore.Deserialize(
            $$"""{ "libraryRoot": "C:\\Sounds", "pushToTalkEnabled": true, "pushToTalkBinding": {{bindingJson}} }""");

        Assert.Equal(PushToTalkBinding.Default, settings.PushToTalkBinding);
        Assert.False(settings.PushToTalkEnabled);
        Assert.Equal(@"C:\Sounds", settings.LibraryRoot);
    }

    [Fact]
    public void Settings_ChordKeyMovedOntoPushToTalk_DisablesPushToTalk()
    {
        var settings = new AppSettings { PushToTalkEnabled = true } with { ChordKey = V };

        var sanitized = settings.Sanitized();

        Assert.False(sanitized.PushToTalkEnabled);
        Assert.Equal(V, sanitized.ChordKey);
    }

    [Fact]
    public void Settings_ValidBinding_IsKeptThroughSanitizing()
    {
        var settings = new AppSettings
        {
            PushToTalkEnabled = true,
            PushToTalkBinding = PushToTalkBinding.FromMouse(PushToTalkMouseButton.Middle),
        };

        Assert.Equal(settings, settings.Sanitized());
    }
}
