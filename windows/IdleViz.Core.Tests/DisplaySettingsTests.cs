namespace IdleViz.Core.Tests;

public sealed class DisplaySettingsTests : IDisposable
{
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
    public void BrightnessDefaultsTo70Percent()
    {
        Assert.Equal(0.7, BrightnessSetting.Value(Store));
        Assert.Equal("70%", BrightnessSetting.Label(BrightnessSetting.Value(Store)));
    }

    [Theory]
    [InlineData(0.2, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.849, 0.85)]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(double.NaN, 0.7)]
    [InlineData(double.PositiveInfinity, 0.7)]
    public void BrightnessStaysInsideTheSlidersRange(double value, double expected) =>
        Assert.Equal(expected, BrightnessSetting.Normalized(value));

    [Fact]
    public void BrightnessIsReadFromTheSettings()
    {
        var settings = Store;
        settings.SetDouble(BrightnessSetting.Key, 0.9);
        Assert.Equal(0.9, BrightnessSetting.Value(settings));
        settings.SetDouble(BrightnessSetting.Key, 0.1);
        Assert.Equal(0.5, BrightnessSetting.Value(settings));
        settings.SetString(BrightnessSetting.Key, "bright");
        Assert.Equal(0.7, BrightnessSetting.Value(settings));
    }

    [Fact]
    public void BrightnessLabelIsWholePercent()
    {
        Assert.Equal("50%", BrightnessSetting.Label(0.5));
        Assert.Equal("85%", BrightnessSetting.Label(0.85));
        Assert.Equal("100%", BrightnessSetting.Label(3));
    }

    [Fact]
    public void BrightnessScript()
    {
        Assert.Equal("window.setBrightness?.(0.85)", BrightnessSetting.Script(0.85));
        Assert.Equal("window.setBrightness?.(1.0)", BrightnessSetting.Script(9));
        Assert.Equal("window.setBrightness?.(0.7)", BrightnessSetting.Script(0.7));
    }

    [Fact]
    public void TheOverlayIsOnUnlessSwitchedOff()
    {
        var settings = Store;
        Assert.True(OverlaySetting.Value(settings));
        settings.SetBool(OverlaySetting.Key, false);
        Assert.False(OverlaySetting.Value(settings));
        settings.SetString(OverlaySetting.Key, "no");
        Assert.True(OverlaySetting.Value(settings));
    }

    [Fact]
    public void ThePresetTitleIsOffUnlessSwitchedOn()
    {
        var settings = Store;
        Assert.False(PresetTitleSetting.Value(settings));
        settings.SetBool(PresetTitleSetting.Key, true);
        Assert.True(PresetTitleSetting.Value(settings));
        settings.SetString(PresetTitleSetting.Key, "yes");
        Assert.False(PresetTitleSetting.Value(settings));
    }

    [Fact]
    public void PresetTitleScript()
    {
        Assert.Equal("window.setPresetTitleEnabled?.(true)", PresetTitleSetting.Script(true));
        Assert.Equal("window.setPresetTitleEnabled?.(false)", PresetTitleSetting.Script(false));
    }

    [Fact]
    public void OverlayScript()
    {
        Assert.Equal("window.setOverlayEnabled?.(true)", OverlaySetting.Script(true));
        Assert.Equal("window.setOverlayEnabled?.(false)", OverlaySetting.Script(false));
    }
}
