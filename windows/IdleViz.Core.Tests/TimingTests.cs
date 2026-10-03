namespace IdleViz.Core.Tests;

public class TimingSettingsTests
{
    [Fact]
    public void Defaults()
    {
        var settings = TimingSettings.Read(new SettingsStore());
        Assert.Equal(300, settings.IdleTimeout(onBattery: false));
        Assert.Equal(3600, settings.KeepAwakeLimit(onBattery: false));
        Assert.False(settings.UseBatteryTimes);
    }

    [Fact]
    public void BatteryValuesOnlyApplyOnBatteryWithTheSwitchOn()
    {
        var settings = new TimingSettings(IdleMinutes: 10, KeepAwakeMinutes: 60, UseBatteryTimes: true, BatteryIdleMinutes: 5, BatteryKeepAwakeMinutes: 30);
        Assert.Equal(600, settings.IdleTimeout(onBattery: false));
        Assert.Equal(3600, settings.KeepAwakeLimit(onBattery: false));
        Assert.Equal(300, settings.IdleTimeout(onBattery: true));
        Assert.Equal(1800, settings.KeepAwakeLimit(onBattery: true));

        var off = settings with { UseBatteryTimes = false };
        Assert.Equal(600, off.IdleTimeout(onBattery: true));
        Assert.Equal(3600, off.KeepAwakeLimit(onBattery: true));
    }

    [Fact]
    public void TheIdleTriggerCanBeOffOnBatteryOnly()
    {
        var settings = new TimingSettings(10, 60, UseBatteryTimes: true, BatteryIdleMinutes: 0, BatteryKeepAwakeMinutes: 60);
        Assert.Equal(600, settings.IdleTimeout(onBattery: false));
        Assert.Null(settings.IdleTimeout(onBattery: true));
    }

    [Fact]
    public void UnsetBatteryValuesAreCopiesOfThePluggedInOnes()
    {
        var store = new SettingsStore();
        store.SetInt(IdleTimeoutSetting.Key, 15);
        store.SetInt(KeepAwakeSetting.Key, 120);
        store.SetBool(BatteryTimesSetting.EnabledKey, true);
        var settings = TimingSettings.Read(store);
        Assert.Equal(900, settings.IdleTimeout(onBattery: true));
        Assert.Equal(7200, settings.KeepAwakeLimit(onBattery: true));
    }

    [Fact]
    public void ReadsStoredBatteryValues()
    {
        var store = new SettingsStore();
        store.SetBool(BatteryTimesSetting.EnabledKey, true);
        store.SetInt(BatteryTimesSetting.IdleTimeoutKey, 0);
        store.SetInt(BatteryTimesSetting.KeepAwakeKey, 30);
        var settings = TimingSettings.Read(store);
        Assert.Null(settings.IdleTimeout(onBattery: true));
        Assert.Equal(1800, settings.KeepAwakeLimit(onBattery: true));
        Assert.Equal(300, settings.IdleTimeout(onBattery: false));
    }

    [Fact]
    public void AKeepAwakeLimitOfZeroFallsBack()
    {
        var store = new SettingsStore();
        store.SetInt(KeepAwakeSetting.Key, 0);
        store.SetInt(BatteryTimesSetting.KeepAwakeKey, -5);
        store.SetBool(BatteryTimesSetting.EnabledKey, true);
        var settings = TimingSettings.Read(store);
        Assert.Equal(3600, settings.KeepAwakeLimit(onBattery: false));
        Assert.Equal(3600, settings.KeepAwakeLimit(onBattery: true));
    }

    [Fact]
    public void ValuesOfTheWrongTypeFallBack()
    {
        var store = new SettingsStore();
        store.SetString(KeepAwakeSetting.Key, "long");
        store.SetString(BatteryTimesSetting.EnabledKey, "yes");
        var settings = TimingSettings.Read(store);
        Assert.Equal(3600, settings.KeepAwakeLimit(onBattery: true));
        Assert.False(settings.UseBatteryTimes);
    }

    [Fact]
    public void RemainingCountsFromWhenTheWindowOpened()
    {
        Assert.Equal(3000, TimingSettings.Remaining(limit: 3600, openedAt: 1000, now: 1600));
        Assert.Equal(0, TimingSettings.Remaining(limit: 3600, openedAt: 1000, now: 4600));
        // A shorter limit applied late (unplugged while open) is already over.
        Assert.True(TimingSettings.Remaining(limit: 1800, openedAt: 1000, now: 3400) < 0);
    }

    [Fact]
    public void KeepAwakeLabels()
    {
        Assert.Equal(["30 min", "1 hour", "2 hours", "4 hours"], KeepAwakeSetting.Choices.Select(KeepAwakeSetting.Label));
        Assert.Contains(KeepAwakeSetting.DefaultMinutes, KeepAwakeSetting.Choices);
    }
}

public class PowerStatusTests
{
    // GetSystemPowerStatus: the AC line is 0 offline, 1 online, 255 unknown. The battery flag is
    // 1 high, 2 low, 4 critical, 8 charging, 128 no battery, 255 unknown.
    [Fact]
    public void ALaptopHasABattery()
    {
        Assert.True(PowerStatus.HasBattery(batteryFlag: 1, PlatformRole.Mobile));
        Assert.True(PowerStatus.HasBattery(batteryFlag: 8, PlatformRole.Mobile));
        // Between high and low the flag is 0.
        Assert.True(PowerStatus.HasBattery(batteryFlag: 0, PlatformRole.Slate));
    }

    [Fact]
    public void ADesktopHasNone()
    {
        Assert.False(PowerStatus.HasBattery(batteryFlag: 128, PlatformRole.Desktop));
        Assert.False(PowerStatus.HasBattery(batteryFlag: 255, PlatformRole.Desktop));
        Assert.False(PowerStatus.HasBattery(batteryFlag: 128, PlatformRole.Mobile));
    }

    [Theory]
    [InlineData(PlatformRole.Desktop)]
    [InlineData(PlatformRole.Workstation)]
    [InlineData(PlatformRole.SohoServer)]
    [InlineData(PlatformRole.Unspecified)]
    public void AUpsDoesNotCountAsABattery(PlatformRole role)
    {
        Assert.False(PowerStatus.HasBattery(batteryFlag: 1, role));
        Assert.False(PowerStatus.OnBattery(acLineStatus: 0, batteryFlag: 1, role));
    }

    [Fact]
    public void OnBatteryOnlyWhileUnplugged()
    {
        Assert.True(PowerStatus.OnBattery(acLineStatus: 0, batteryFlag: 1, PlatformRole.Mobile));
        Assert.False(PowerStatus.OnBattery(acLineStatus: 1, batteryFlag: 9, PlatformRole.Mobile));
        Assert.False(PowerStatus.OnBattery(acLineStatus: 255, batteryFlag: 1, PlatformRole.Mobile));
        Assert.False(PowerStatus.OnBattery(acLineStatus: 0, batteryFlag: 128, PlatformRole.Mobile));
    }
}

public class DisplayLayoutTests
{
    private static readonly Display s_laptop = new(@"\\.\DISPLAY1", 0, 0, 1920, 1200, 144);
    private static readonly Display s_external = new(@"\\.\DISPLAY2", 1920, 0, 2560, 1440, 96);

    [Fact]
    public void StaysOpenWhenNothingAboutTheDisplaysChanged()
    {
        var tracker = new DisplayTracker([s_laptop]);
        // The taskbar moving or a wallpaper change sends the same notice.
        Assert.False(tracker.ShouldClose([s_laptop]));
        Assert.False(tracker.ShouldClose([s_laptop with { }]));
    }

    [Fact]
    public void ClosesWhenADisplayIsAddedOrRemoved()
    {
        var tracker = new DisplayTracker([s_laptop]);
        Assert.True(tracker.ShouldClose([s_laptop, s_external]));
        Assert.True(tracker.ShouldClose([s_laptop]));
    }

    [Fact]
    public void ClosesWhenTheResolutionScaleOrPositionChanges()
    {
        var tracker = new DisplayTracker([s_laptop, s_external]);
        Assert.True(tracker.ShouldClose([s_laptop with { Width = 1680, Height = 1050 }, s_external]));
        Assert.True(tracker.ShouldClose([s_laptop with { Width = 1680, Height = 1050, Dpi = 96 }, s_external]));
        Assert.True(tracker.ShouldClose([s_laptop with { Width = 1680, Height = 1050, Dpi = 96 }, s_external with { Left = -2560 }]));
    }

    [Fact]
    public void ClosesWhenAnotherDisplayBecomesThePrimaryOne()
    {
        var tracker = new DisplayTracker([s_laptop, s_external]);
        Assert.True(tracker.ShouldClose([s_external, s_laptop]));
    }

    [Fact]
    public void RemembersTheNewLayoutAfterAChange()
    {
        var tracker = new DisplayTracker([s_laptop]);
        Display[] both = [s_laptop, s_external];
        Assert.True(tracker.ShouldClose(both));
        // The same layout again is no longer a change.
        Assert.False(tracker.ShouldClose([s_laptop, s_external]));
        Assert.Equal(both, tracker.Layout);
    }
}
