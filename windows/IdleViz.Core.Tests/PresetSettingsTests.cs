using System.Text.Json;

namespace IdleViz.Core.Tests;

public sealed class PresetSettingsTests
{
    [Fact]
    public void Defaults()
    {
        var settings = PresetSettings.Read(new SettingsStore());
        Assert.Equal(PresetMode.Shuffle, settings.Mode);
        Assert.Equal(PresetSettings.DefaultSingle, settings.SinglePreset);
        Assert.Equal(ShuffleSource.All, settings.ShuffleFrom);
        Assert.Equal(30, settings.SecondsPerPreset);
        Assert.Equal(2.7, settings.BlendSeconds);
        Assert.Empty(settings.Favorites);
        Assert.Empty(settings.Blocked);
    }

    [Fact]
    public void SavesAndLoads()
    {
        var store = new SettingsStore();
        var settings = new PresetSettings
        {
            Mode = PresetMode.Single,
            SinglePreset = "custom:Mine",
            ShuffleFrom = ShuffleSource.Favorites,
            SecondsPerPreset = 120,
            BlendSeconds = 0,
        };
        settings.SetFavorite("bundled:A", true);
        settings.SetFavorite("bundled:B", true);
        settings.SetBlocked("bundled:C", true);
        settings.Save(store);

        var read = PresetSettings.Read(store);
        Assert.Equal(settings.Script, read.Script);
        Assert.Equal("single", store.GetString("visualizerMode"));
        Assert.Equal("favorites", store.GetString("shuffleFrom"));
        Assert.Equal(["bundled:A", "bundled:B"], store.GetStringList("favoritePresets"));
    }

    [Fact]
    public void UnusableStoredValuesFallBack()
    {
        var store = new SettingsStore();
        store.SetString("visualizerMode", "sideways");
        store.SetString("shuffleFrom", "everything");
        store.SetInt("secondsPerPreset", 7);
        store.SetDouble("blendSeconds", 99.5);
        store.SetString("singlePreset", string.Empty);
        store.SetStringList("favoritePresets", ["bundled:A", "bundled:A", "bundled:B"]);
        store.SetStringList("blockedPresets", ["bundled:B", "bundled:C"]);

        var settings = PresetSettings.Read(store);
        Assert.Equal(PresetMode.Shuffle, settings.Mode);
        Assert.Equal(ShuffleSource.All, settings.ShuffleFrom);
        Assert.Equal(30, settings.SecondsPerPreset);
        Assert.Equal(2.7, settings.BlendSeconds);
        Assert.Equal(PresetSettings.DefaultSingle, settings.SinglePreset);
        Assert.Equal(["bundled:A", "bundled:B"], settings.Favorites);
        Assert.Equal(["bundled:C"], settings.Blocked);
    }

    [Fact]
    public void ReadsTheMacStyleNamesOnly()
    {
        var store = new SettingsStore();
        store.SetString("visualizerMode", "Single");
        Assert.Equal(PresetMode.Shuffle, PresetSettings.Read(store).Mode);
        store.SetString("visualizerMode", "single");
        Assert.Equal(PresetMode.Single, PresetSettings.Read(store).Mode);
    }

    [Fact]
    public void APresetIsNeverBothFavoriteAndBlocked()
    {
        var settings = new PresetSettings();
        settings.SetFavorite("bundled:A", true);
        settings.SetFavorite("bundled:A", true);
        Assert.Equal(["bundled:A"], settings.Favorites);
        settings.SetBlocked("bundled:A", true);
        Assert.Empty(settings.Favorites);
        Assert.Equal(["bundled:A"], settings.Blocked);
        settings.SetFavorite("bundled:A", true);
        Assert.Empty(settings.Blocked);
        Assert.True(settings.IsFavorite("bundled:A"));
        settings.SetFavorite("bundled:A", false);
        Assert.False(settings.IsFavorite("bundled:A") || settings.IsBlocked("bundled:A"));
    }

    [Fact]
    public void ScriptCarriesEverythingAndEscapesNames()
    {
        var settings = new PresetSettings { Mode = PresetMode.Single, SinglePreset = "custom:it's \"quoted\" \\ \u2028" };
        settings.SetBlocked("bundled:</script>", true);
        var script = settings.Script;
        const string prefix = "window.setPresetSettings?.(";
        Assert.StartsWith(prefix + "{", script, StringComparison.Ordinal);
        Assert.EndsWith("})", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2028", script, StringComparison.Ordinal);
        Assert.DoesNotContain("</script>", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'", script, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(script[prefix.Length..^1]);
        var root = json.RootElement;
        Assert.Equal("single", root.GetProperty("mode").GetString());
        Assert.Equal(settings.SinglePreset, root.GetProperty("single").GetString());
        Assert.Equal("all", root.GetProperty("shuffleFrom").GetString());
        Assert.Equal(30, root.GetProperty("secondsPerPreset").GetInt32());
        Assert.Equal(2.7, root.GetProperty("blendSeconds").GetDouble());
        Assert.Equal("bundled:</script>", root.GetProperty("blocked")[0].GetString());
        Assert.Equal(0, root.GetProperty("favorites").GetArrayLength());
        Assert.Equal(
            ["blendSeconds", "blocked", "favorites", "mode", "secondsPerPreset", "shuffleFrom", "single"],
            root.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void ChoicesIncludeTheDefaults()
    {
        Assert.Contains(new PresetSettings().SecondsPerPreset, PresetSettings.SecondsChoices);
        Assert.Contains(new PresetSettings().BlendSeconds, PresetSettings.BlendChoices);
    }

    [Fact]
    public void EmptySourceHint()
    {
        var settings = new PresetSettings();
        Assert.Null(settings.EmptySourceHint(hasCustomPresets: false));
        settings.ShuffleFrom = ShuffleSource.Favorites;
        Assert.Equal("No favorites yet, so all presets are used", settings.EmptySourceHint(hasCustomPresets: false));
        settings.SetFavorite("bundled:A", true);
        Assert.Null(settings.EmptySourceHint(hasCustomPresets: false));
        settings.ShuffleFrom = ShuffleSource.Custom;
        Assert.Equal("No custom presets, so all presets are used", settings.EmptySourceHint(hasCustomPresets: false));
        Assert.Null(settings.EmptySourceHint(hasCustomPresets: true));
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(1, "1 s")]
    [InlineData(2.7, "2.7 s")]
    public void BlendLabel(double seconds, string label) => Assert.Equal(label, PresetSettings.BlendLabel(seconds));

    [Theory]
    [InlineData(15, "15 s")]
    [InlineData(45, "45 s")]
    [InlineData(60, "1 min")]
    [InlineData(90, "90 s")]
    [InlineData(300, "5 min")]
    public void SecondsLabel(int seconds, string label) => Assert.Equal(label, PresetSettings.SecondsLabel(seconds));
}

public sealed class PresetInfoTests
{
    [Fact]
    public void ReadsTheList()
    {
        const string reply = """[{"id":"bundled:A","name":"A","source":"bundled"},{"id":"custom:B","name":"B","source":"custom"}]""";
        Assert.Equal([new PresetInfo("bundled:A", "A", "bundled"), new PresetInfo("custom:B", "B", "custom")], PresetInfo.List(reply));
    }

    [Fact]
    public void DropsWrongShapesAndRepeats()
    {
        var longName = new string('n', 1000);
        var longId = new string('i', 301);
        var reply = $$"""
            ["text", 4, {"name":"no id"}, {"id":"","name":"empty"}, {"id":"{{longId}}","name":"long id"},
             {"id":"bundled:A","name":"{{longName}}","source":"elsewhere"},
             {"id":"bundled:A","name":"again"}]
            """;
        var list = PresetInfo.List(reply);
        Assert.Single(list);
        Assert.Equal(PresetInfo.MaxTextLength, list[0].Name.Length);
        Assert.Equal("bundled", list[0].Source);
        Assert.Empty(PresetInfo.List("\"nope\""));
        Assert.Empty(PresetInfo.List("null"));
        Assert.Empty(PresetInfo.List(null));
        Assert.Empty(PresetInfo.List("[[[[[[[[[[1]]]]]]]]]]"));
        Assert.Empty(PresetInfo.List("not json"));
    }

    [Fact]
    public void FallbackName()
    {
        Assert.Equal("My: Preset", PresetInfo.FallbackName("custom:My: Preset"));
        Assert.Equal("plain", PresetInfo.FallbackName("plain"));
    }
}

public sealed class PresetListTests
{
    [Fact]
    public void EachListSetsItsOwnIds()
    {
        var settings = new PresetSettings();
        PresetList.Favorites.Set(settings, "bundled:A", true);
        Assert.Equal(["bundled:A"], PresetList.Favorites.Ids(settings));
        PresetList.Blocklist.Set(settings, "bundled:A", true);
        Assert.Empty(PresetList.Favorites.Ids(settings));
        Assert.Equal(["bundled:A"], PresetList.Blocklist.Ids(settings));
        PresetList.Blocklist.Set(settings, "bundled:A", false);
        Assert.Empty(PresetList.Blocklist.Ids(settings));
    }

    [Fact]
    public void Texts()
    {
        Assert.Equal("Favorites", PresetList.Favorites.Title());
        Assert.Equal("Blocklist", PresetList.Blocklist.Title());
        Assert.StartsWith("No favorites yet", PresetList.Favorites.EmptyText(), StringComparison.Ordinal);
        Assert.StartsWith("Nothing blocked", PresetList.Blocklist.EmptyText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SearchMatchesPartOfTheNameIgnoringCase()
    {
        IReadOnlyList<PresetInfo> presets = [new("bundled:A", "Geiss - Swirlie 5", "bundled"), new("custom:B", "Aurora", "custom")];
        Assert.Same(presets, PresetLists.Search(presets, "  "));
        Assert.Equal(["bundled:A"], PresetLists.Search(presets, " swirl ").Select(preset => preset.Id));
        Assert.Equal(["custom:B"], PresetLists.Search(presets, "AUR").Select(preset => preset.Id));
        Assert.Empty(PresetLists.Search(presets, "zylot"));
    }
}
