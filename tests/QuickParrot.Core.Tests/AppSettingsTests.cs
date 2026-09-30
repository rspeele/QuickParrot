using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var settings = new AppSettings
        {
            LibraryRoot = @"C:\Sounds",
            NavigatorPersistentPath = "Movies/Arnold",
            ChordKey = new ScanKey(0x47, true),
            HotkeysEnabled = false,
            CableDeviceId = "{0.0.0.00000000}.{cable}",
            MonitorDeviceId = null,
            CableVolume = 0.8f,
            MonitorVolume = 0.25f,
            PushToTalkEnabled = true,
            PushToTalkKey = "V",
            MicMuteEnabled = true,
            PreRollMilliseconds = 300,
            PostRollMilliseconds = 750,
        };

        var roundTripped = JsonSettingsStore.Deserialize(JsonSettingsStore.Serialize(settings));

        Assert.Equal(settings, roundTripped);
    }

    [Fact]
    public void MissingFields_GetDefaults()
    {
        var settings = JsonSettingsStore.Deserialize("""{ "libraryRoot": "C:\\Sounds" }""");

        Assert.Equal(@"C:\Sounds", settings.LibraryRoot);
        Assert.Equal("", settings.NavigatorPersistentPath);
        Assert.Equal(ScanKey.DefaultChordKey, settings.ChordKey);
        Assert.True(settings.HotkeysEnabled);
        Assert.Equal(1f, settings.CableVolume);
        Assert.Equal(500, settings.PreRollMilliseconds);
        Assert.Equal(500, settings.PostRollMilliseconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    public void CorruptJson_GivesDefaults(string json)
    {
        Assert.False(JsonSettingsStore.TryDeserialize(json, out var settings));
        Assert.Equal(new AppSettings(), settings);
        Assert.Equal(new AppSettings(), JsonSettingsStore.Deserialize(json));
    }

    [Fact]
    public void OutOfRangeValues_AreClamped()
    {
        var json = """
            { "cableVolume": 7, "monitorVolume": -1, "preRollMilliseconds": -5,
              "postRollMilliseconds": 999999, "navigatorPersistentPath": null }
            """;

        var settings = JsonSettingsStore.Deserialize(json);

        Assert.Equal(1f, settings.CableVolume);
        Assert.Equal(0f, settings.MonitorVolume);
        Assert.Equal(0, settings.PreRollMilliseconds);
        Assert.Equal(AppSettings.MaxMarginMilliseconds, settings.PostRollMilliseconds);
        Assert.Equal("", settings.NavigatorPersistentPath);
    }

    [Fact]
    public void ChordKey_IsStoredAsScanCodeAndExtendedFlag()
    {
        var json = JsonSettingsStore.Serialize(new AppSettings { ChordKey = new ScanKey(0x0C, false) });

        Assert.Contains("\"chordKey\": {", json);
        Assert.Contains("\"scanCode\": 12", json);
        Assert.DoesNotContain("digit", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new ScanKey(0x0C, false), JsonSettingsStore.Deserialize("""{ "chordKey": { "scanCode": 12 } }""").ChordKey);
    }

    [Theory]
    [InlineData("""{ "scanCode": 2, "isExtended": false }""")] // the 1 key
    [InlineData("""{ "scanCode": 42, "isExtended": false }""")] // left shift
    [InlineData("""{ "scanCode": 0 }""")]
    [InlineData("""{ "scanCode": 99999 }""")]
    [InlineData("""{ }""")]
    public void InvalidChordKey_FallsBackToDefault(string chordKeyJson)
    {
        var settings = JsonSettingsStore.Deserialize($$"""{ "libraryRoot": "C:\\Sounds", "chordKey": {{chordKeyJson}} }""");

        Assert.Equal(ScanKey.DefaultChordKey, settings.ChordKey);
        Assert.Equal(@"C:\Sounds", settings.LibraryRoot);
    }

    [Fact]
    public void ConvertsToPlaybackAndOutputSettings()
    {
        var settings = new AppSettings
        {
            PreRollMilliseconds = 250,
            PushToTalkEnabled = true,
            CableDeviceId = "cable",
            MonitorVolume = 0.5f,
        };

        var playback = settings.ToPlaybackOptions();
        var output = settings.ToOutputSettings();

        Assert.Equal(TimeSpan.FromMilliseconds(250), playback.PreRoll);
        Assert.True(playback.PushToTalkEnabled);
        Assert.False(playback.MicMuteEnabled);
        Assert.Equal("cable", output.CableDeviceId);
        Assert.Null(output.MonitorDeviceId);
        Assert.Equal(0.5f, output.MonitorVolume);
    }

    [Fact]
    public void Store_SavesAndLoadsFile()
    {
        var directory = Directory.CreateTempSubdirectory("quickparrot-settings-");
        try
        {
            var store = new JsonSettingsStore(Path.Combine(directory.FullName, "nested", "settings.json"));
            Assert.Equal(new SettingsLoadResult(new AppSettings()), store.Load());

            store.Save(new AppSettings { LibraryRoot = @"C:\Sounds" });
            store.Save(new AppSettings { LibraryRoot = @"D:\Clips" });

            Assert.Equal(@"D:\Clips", store.Load().Settings.LibraryRoot);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Store_SetsAsideCorruptFile_AndWarns()
    {
        var directory = Directory.CreateTempSubdirectory("quickparrot-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            var store = new JsonSettingsStore(path);
            File.WriteAllText(store.BackupPath, "older corrupt file");
            File.WriteAllText(path, "{ not json");

            var result = store.Load();

            Assert.Equal(new AppSettings(), result.Settings);
            Assert.Contains(store.BackupPath, result.Warning);
            Assert.False(File.Exists(path));
            Assert.Equal("{ not json", File.ReadAllText(store.BackupPath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
