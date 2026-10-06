namespace IdleViz.Core.Tests;

public sealed class VisualizerKeysTests : IDisposable
{
    private const ushort KeyB = 0x42;
    private const ushort KeyF = 0x46;
    private const ushort KeyL = 0x4C;
    private const ushort KeyN = 0x4E;
    private const ushort Space = 0x20;
    private const ushort Escape = 0x1B;
    private const ushort F5 = 0x74;

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
    public void DefaultsAreLNAndB()
    {
        var keys = VisualizerKeys.Read(Store);
        Assert.Equal(KeyL, keys.Like);
        Assert.Equal(KeyN, keys.Skip);
        Assert.Equal(KeyB, keys.Block);
        Assert.Equal(new HashSet<ushort> { KeyL, KeyN, KeyB }, keys.Codes);
    }

    [Fact]
    public void ReadsStoredKeys()
    {
        var settings = Store;
        settings.SetInt(VisualizerKeys.LikeKey, KeyF);
        settings.SetInt(VisualizerKeys.SkipKey, Space);
        settings.SetInt(VisualizerKeys.BlockKey, F5);
        var keys = VisualizerKeys.Read(settings);
        Assert.Equal(KeyF, keys.Like);
        Assert.Equal(Space, keys.Skip);
        Assert.Equal(F5, keys.Block);
    }

    [Fact]
    public void OffMeansNoKey()
    {
        var settings = Store;
        settings.SetInt(VisualizerKeys.LikeKey, VisualizerKeys.Off);
        settings.SetInt(VisualizerKeys.SkipKey, VisualizerKeys.Off);
        settings.SetInt(VisualizerKeys.BlockKey, VisualizerKeys.Off);
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
    public void AnOlderChoiceKeepsTheKeyTheBlockKeyDefaultsTo()
    {
        var settings = Store;
        settings.SetInt(VisualizerKeys.SkipKey, KeyB);
        var keys = VisualizerKeys.Read(settings);
        Assert.Equal(KeyB, keys.Skip);
        Assert.Null(keys.Block);
        Assert.Equal(VisualizerAction.Skip, keys.Action(KeyB));
    }

    [Fact]
    public void EachKeyHasItsAction()
    {
        var keys = new VisualizerKeys();
        Assert.Equal(VisualizerAction.Like, keys.Action(KeyL));
        Assert.Equal(VisualizerAction.Skip, keys.Action(KeyN));
        Assert.Equal(VisualizerAction.Block, keys.Action(KeyB));
        Assert.Null(keys.Action(KeyF));
    }

    [Theory]
    [InlineData(0x41, true)] // A
    [InlineData(0x20, true)] // Space
    [InlineData(0x74, true)] // F5
    [InlineData(0xBA, true)] // ;
    [InlineData(0x0D, true)] // Enter
    [InlineData(0x1B, false)] // Esc cancels the recorder
    [InlineData(0xA2, false)] // Left Ctrl
    [InlineData(0x10, false)] // Shift
    [InlineData(0x5B, false)] // Left Windows key
    [InlineData(0xB3, false)] // Play/pause
    [InlineData(0xAF, false)] // Volume up
    [InlineData(0x01, false)] // Left mouse button
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(100_000, false)]
    public void AlmostAnyKeyCanBeRecorded(int code, bool expected) => Assert.Equal(expected, VisualizerKeys.CanBe(code));

    [Fact]
    public void LabelsNameTheKey()
    {
        Assert.Equal("L", VisualizerKeys.Label(KeyL));
        Assert.Equal("Space", VisualizerKeys.Label(Space));
        Assert.Equal("F5", VisualizerKeys.Label(F5));
        Assert.Equal("Not set", VisualizerKeys.Label(null));
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
        Assert.False(tracker.ShouldDismiss(InputEvent.Key(KeyB, isDown: true, isRepeat: false), 1.25));
        Assert.True(tracker.ShouldDismiss(InputEvent.Key(KeyF, isDown: true, isRepeat: false), 1.3));
    }
}
