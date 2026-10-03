namespace IdleViz.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "IdleVizTests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AMissingFileIsEmpty()
    {
        var settings = new SettingsStore(FilePath);
        Assert.Null(settings.GetInt("idleTimeout"));
        Assert.Null(settings.GetBool("showOverlay"));
        Assert.Null(settings.GetString("visualizerMode"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ValuesSurviveAReload()
    {
        var settings = new SettingsStore(FilePath);
        settings.SetInt("idleTimeout", 10);
        settings.SetBool("showOverlay", false);
        settings.SetString("visualizerMode", "single");

        var reloaded = new SettingsStore(FilePath);
        Assert.Equal(10, reloaded.GetInt("idleTimeout"));
        Assert.False(reloaded.GetBool("showOverlay"));
        Assert.Equal("single", reloaded.GetString("visualizerMode"));
    }

    [Fact]
    public void AValueOfTheWrongTypeReadsAsNotSet()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "idleTimeout": "ten", "showOverlay": 1, "visualizerMode": true, "keepAwakeLimit": 1.5 }""");
        var settings = new SettingsStore(FilePath);
        Assert.Null(settings.GetInt("idleTimeout"));
        Assert.Null(settings.GetBool("showOverlay"));
        Assert.Null(settings.GetString("visualizerMode"));
        Assert.Null(settings.GetInt("keepAwakeLimit"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("")]
    public void AnUnreadableFileIsEmptyAndGetsReplaced(string contents)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, contents);
        var settings = new SettingsStore(FilePath);
        Assert.Null(settings.GetInt("idleTimeout"));

        settings.SetInt("idleTimeout", 15);
        Assert.Equal(15, new SettingsStore(FilePath).GetInt("idleTimeout"));
    }

    [Fact]
    public void ChangedIsRaisedOnlyForRealChanges()
    {
        var settings = new SettingsStore();
        var changes = new List<string>();
        settings.Changed += (_, change) => changes.Add(change.Key);

        settings.SetInt("idleTimeout", 10);
        settings.SetInt("idleTimeout", 10);
        settings.SetInt("idleTimeout", 15);
        settings.Remove("idleTimeout");
        settings.Remove("idleTimeout");

        Assert.Equal(["idleTimeout", "idleTimeout", "idleTimeout"], changes);
        Assert.Null(settings.GetInt("idleTimeout"));
    }

    [Fact]
    public void DoublesAndListsSurviveAReload()
    {
        var settings = new SettingsStore(FilePath);
        settings.SetDouble("blendSeconds", 2.7);
        settings.SetStringList("favoritePresets", ["bundled:A", "custom:B"]);

        var reloaded = new SettingsStore(FilePath);
        Assert.Equal(2.7, reloaded.GetDouble("blendSeconds"));
        Assert.Equal(["bundled:A", "custom:B"], reloaded.GetStringList("favoritePresets"));
        // An int reads as a double too, as JSON doesn't tell them apart.
        reloaded.SetInt("blendSeconds", 5);
        Assert.Equal(5.0, reloaded.GetDouble("blendSeconds"));
    }

    [Fact]
    public void ListsSkipEntriesOfTheWrongType()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "favoritePresets": ["bundled:A", 4, null, {"x": 1}, "bundled:B"], "blockedPresets": "bundled:C", "blendSeconds": "2.7" }""");
        var settings = new SettingsStore(FilePath);
        Assert.Equal(["bundled:A", "bundled:B"], settings.GetStringList("favoritePresets"));
        Assert.Null(settings.GetStringList("blockedPresets"));
        Assert.Null(settings.GetDouble("blendSeconds"));
    }

    [Fact]
    public void ChangedIsRaisedOnlyWhenAListChanges()
    {
        var settings = new SettingsStore();
        var changes = 0;
        settings.Changed += (_, _) => changes++;
        settings.SetStringList("favoritePresets", ["bundled:A"]);
        settings.SetStringList("favoritePresets", ["bundled:A"]);
        settings.SetStringList("favoritePresets", ["bundled:A", "bundled:B"]);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void MapsOfNumbersSurviveAReloadAndSkipEntriesOfTheWrongType()
    {
        var settings = new SettingsStore(FilePath);
        settings.SetDoubleMap("audioDelayByDevice", new Dictionary<string, double> { ["speakers"] = 0.02, ["headphones"] = 0.21 });
        Assert.Equal(
            new Dictionary<string, double> { ["speakers"] = 0.02, ["headphones"] = 0.21 },
            new SettingsStore(FilePath).GetDoubleMap("audioDelayByDevice"));

        File.WriteAllText(FilePath, """{ "audioDelayByDevice": { "a": 0.2, "b": "text", "c": 1, "d": null, "e": [1] }, "idleTimeout": 5 }""");
        var reloaded = new SettingsStore(FilePath);
        Assert.Equal(new Dictionary<string, double> { ["a"] = 0.2, ["c"] = 1 }, reloaded.GetDoubleMap("audioDelayByDevice"));
        Assert.Empty(reloaded.GetDoubleMap("idleTimeout"));
        Assert.Empty(reloaded.GetDoubleMap("missing"));
    }

    [Fact]
    public void NoTemporaryFileIsLeftBehind()
    {
        var settings = new SettingsStore(FilePath);
        settings.SetInt("idleTimeout", 10);
        Assert.Equal(["settings.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }
}
