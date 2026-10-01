using QuickParrot.Core.Favorites;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests.Favorites;

public class FavoriteSlotsTests
{
    [Theory]
    [InlineData("Trump/wall.wav", "Trump/wall.wav")]
    [InlineData(@"Trump\Sub\wall.wav", "Trump/Sub/wall.wav")]
    [InlineData("Trump//wall.wav/", "Trump/wall.wav")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("/abs.wav", null)]
    [InlineData(@"\\server\share\a.wav", null)]
    [InlineData(@"C:\Sounds\a.wav", null)]
    [InlineData("C:a.wav", null)]
    [InlineData("../secret.wav", null)]
    [InlineData("a/../../b.wav", null)]
    [InlineData("a/./b.wav", null)]
    [InlineData("a/.../b.wav", null)]
    public void NormalizePath(string input, string? expected) =>
        Assert.Equal(expected, FavoriteSlots.NormalizePath(input));

    [Fact]
    public void AlwaysHasTwelveSlots_DroppingExtrasAndPaddingMissing()
    {
        Assert.Equal(12, new FavoriteSlots(["a.wav"]).Paths.Count);
        var many = new FavoriteSlots(Enumerable.Range(1, 20).Select(i => $"{i}.wav"));
        Assert.Equal(12, many.Paths.Count);
        Assert.Equal("12.wav", many[12]);
        Assert.Null(new FavoriteSlots(["a.wav"])[2]);
    }

    [Fact]
    public void BadPathsBecomeEmptySlots()
    {
        var slots = new FavoriteSlots(["ok.wav", "../escape.wav", @"D:\x.wav"]);

        Assert.Equal(["ok.wav", null, null], slots.Paths.Take(3));
    }

    [Fact]
    public void With_ReplacesOneSlot_AndLeavesTheOriginalAlone()
    {
        var original = new FavoriteSlots(["a.wav"]);
        var changed = original.With(3, @"Sub\b.wav").With(1, null);

        Assert.Equal("a.wav", original[1]);
        Assert.Null(changed[1]);
        Assert.Equal("Sub/b.wav", changed[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void OutOfRangeSlots_Throw(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FavoriteSlots.Empty[slot]);
        Assert.Throws<ArgumentOutOfRangeException>(() => FavoriteSlots.Empty.With(slot, "a.wav"));
    }

    [Fact]
    public void ComparesByValue()
    {
        Assert.Equal(new FavoriteSlots(["a.wav", null, "b.wav"]), new FavoriteSlots(["a.wav", null, "b.wav"]));
        Assert.Equal(new FavoriteSlots(["a.wav"]).GetHashCode(), new FavoriteSlots(["a.wav"]).GetHashCode());
        Assert.NotEqual(new FavoriteSlots(["a.wav"]), new FavoriteSlots([null, "a.wav"]));
    }

    [Fact]
    public void AssignedMask_HasABitPerFilledSlot() =>
        Assert.Equal(0b1000_0000_0101, FavoriteSlots.Empty.With(1, "a.wav").With(3, "b.wav").With(12, "c.wav").AssignedMask);

    [Theory]
    [InlineData("Trump/wall.wav", "wall")]
    [InlineData("Wilhelm scream.mp3", "Wilhelm scream")]
    [InlineData("Sub/.wav", ".wav")]
    public void DisplayName(string path, string expected) => Assert.Equal(expected, FavoriteSlots.DisplayName(path));

    [Fact]
    public void SettingsRoundTripThroughJson()
    {
        var settings = new AppSettings
        {
            Favorites = FavoriteSlots.Empty.With(1, "Trump/wall.wav").With(12, "boom.mp3"),
            FavoritesWithoutChord = true,
        };

        var json = JsonSettingsStore.Serialize(settings);
        var roundTripped = JsonSettingsStore.Deserialize(json);

        Assert.Equal(settings, roundTripped);
        Assert.Contains("\"Trump/wall.wav\"", json);
    }

    [Fact]
    public void MissingFavorites_DefaultToEmptyAndChordRequired()
    {
        var settings = JsonSettingsStore.Deserialize("""{ "libraryRoot": "C:\\Sounds" }""");

        Assert.Equal(FavoriteSlots.Empty, settings.Favorites);
        Assert.False(settings.FavoritesWithoutChord);
    }

    [Theory]
    [InlineData("""{ "favorites": null }""")]
    [InlineData("""{ "favorites": "F3" }""")]
    [InlineData("""{ "favorites": { "f1": "a.wav" } }""")]
    public void JunkFavorites_LoadAsEmpty_WithoutLosingOtherSettings(string json)
    {
        var withRoot = json.Replace("{ ", """{ "libraryRoot": "C:\\Sounds", """);

        Assert.True(JsonSettingsStore.TryDeserialize(withRoot, out var settings));
        Assert.Equal(FavoriteSlots.Empty, settings.Favorites);
        Assert.Equal(@"C:\Sounds", settings.LibraryRoot);
    }

    [Fact]
    public void HandEditedFavorites_AreSanitizedOnLoad()
    {
        var settings = JsonSettingsStore.Deserialize(
            """{ "favorites": ["a.wav", 7, "../x.wav", ["nested"], "Sub\\b.wav", null, "c.wav", "d", "e", "f", "g", "h", "i", "j"] }""");

        Assert.Equal(["a.wav", null, null, null, "Sub/b.wav", null, "c.wav", "d", "e", "f", "g", "h"], settings.Favorites.Paths);
    }
}
