namespace IdleViz.Core.Tests;

public sealed class VisualizerKeysTests : IDisposable
{
    private const ushort KeyF = 0x46;
    private const ushort KeyL = 0x4C;
    private const ushort KeyN = 0x4E;
    private const ushort Space = 0x20;
    private const ushort Escape = 0x1B;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "idleviz-tests-" + Guid.NewGuid().ToString("N"));

    private SettingsStore Store => new(Path.Combine(_folder, "settings.json"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void DefaultsAreLAndN()
    {
        var keys = VisualizerKeys.Read(Store);
        Assert.Equal(KeyL, keys.Like);
        Assert.Equal(KeyN, keys.Skip);
        Assert.Equal(new HashSet<ushort> { KeyL, KeyN }, keys.Codes);
    }

    [Fact]
    public void ReadsStoredKeys()
    {
        var settings = Store;
        settings.SetInt(VisualizerKeys.LikeKey, KeyF);
        settings.SetInt(VisualizerKeys.SkipKey, Space);
        var keys = VisualizerKeys.Read(settings);
        Assert.Equal(KeyF, keys.Like);
        Assert.Equal(Space, keys.Skip);
    }

    [Fact]
    public void OffMeansNoKey()
    {
        var settings = Store;
        settings.SetInt(VisualizerKeys.LikeKey, VisualizerKeys.Off);
        settings.SetInt(VisualizerKeys.SkipKey, VisualizerKeys.Off);
        var keys = VisualizerKeys.Read(settings);
        Assert.Null(keys.Like);
        Assert.Null(keys.Skip);
        Assert.Empty(keys.Codes);
        Assert.Null(keys.Action(KeyL));
    }

    [Fact]
    public void AnUnusableStoredValueFallsBackToTheDefault()
    {
        var settings = Store;
        settings.SetInt(VisualizerKeys.LikeKey, Escape);
        settings.SetInt(VisualizerKeys.SkipKey, 100_000);
        var keys = VisualizerKeys.Read(settings);
        Assert.Equal(KeyL, keys.Like);
        Assert.Equal(KeyN, keys.Skip);

        settings.SetString(VisualizerKeys.LikeKey, "L");
        Assert.Equal(KeyL, VisualizerKeys.Read(settings).Like);
    }

    [Fact]
    public void OneKeyCannotDoBoth()
    {
        var settings = Store;
        settings.SetInt(VisualizerKeys.LikeKey, KeyF);
        settings.SetInt(VisualizerKeys.SkipKey, KeyF);
        var keys = VisualizerKeys.Read(settings);
        Assert.Equal(KeyF, keys.Like);
        Assert.Null(keys.Skip);
        Assert.Equal(VisualizerAction.Like, keys.Action(KeyF));
    }

    [Fact]
    public void EachKeyHasItsAction()
    {
        var keys = new VisualizerKeys();
        Assert.Equal(VisualizerAction.Like, keys.Action(KeyL));
        Assert.Equal(VisualizerAction.Skip, keys.Action(KeyN));
        Assert.Null(keys.Action(KeyF));
    }

    [Fact]
    public void ThePickersOfferLettersDigitsArrowsAndSpace()
    {
        var choices = VisualizerKey.Choices;
        Assert.Equal(41, choices.Count);
        Assert.Equal(41, choices.Select(key => key.Code).Distinct().Count());
        Assert.Equal("A", choices[0].Label);
        Assert.Equal(0x41, choices[0].Code);
        Assert.Equal("Z", choices[25].Label);
        Assert.Equal("0", choices[26].Label);
        Assert.Equal(0x30, choices[26].Code);
        Assert.Equal("9", choices[35].Label);
        Assert.Equal([0x25, 0x27, 0x26, 0x28], choices.Skip(36).Take(4).Select(key => (int)key.Code));
        Assert.Equal("Space", choices[40].Label);
    }

    [Fact]
    public void PageScripts()
    {
        Assert.Equal("window.skipPreset?.()", VisualizerKeys.SkipScript);
        Assert.Equal("window.showLike?.(true)", VisualizerKeys.LikeScript(true));
        Assert.Equal("window.showLike?.(false)", VisualizerKeys.LikeScript(false));
    }

    [Fact]
    public void TheKeysDoNotCloseTheVisualizer()
    {
        var tracker = new DismissTracker(passKeys: new VisualizerKeys().Codes);
        tracker.Arm([], 0.4);
        Assert.False(tracker.ShouldDismiss(InputEvent.Key(KeyL, isDown: true, isRepeat: false), 1));
        Assert.False(tracker.ShouldDismiss(InputEvent.Key(KeyL, isDown: false, isRepeat: false), 1.1));
        Assert.False(tracker.ShouldDismiss(InputEvent.Key(KeyN, isDown: true, isRepeat: true), 1.2));
        Assert.True(tracker.ShouldDismiss(InputEvent.Key(KeyF, isDown: true, isRepeat: false), 1.3));
    }
}
