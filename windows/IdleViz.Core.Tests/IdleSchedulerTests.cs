namespace IdleViz.Core.Tests;

public class IdleSchedulerTests
{
    [Fact]
    public void OffWithoutATimeout()
    {
        var scheduler = new IdleScheduler(null);
        Assert.Equal(IdleStep.Off, scheduler.Check(now: 1000, idle: 5000));
        scheduler.TimeoutSeconds = 0;
        Assert.Equal(IdleStep.Off, scheduler.Check(now: 1000, idle: 5000));
    }

    [Fact]
    public void WaitsUntilTheTimeoutCouldBeReached() =>
        Assert.Equal(IdleStep.Wait(180), new IdleScheduler(300).Check(now: 1000, idle: 120));

    [Fact]
    public void FiresOnceTheTimeoutIsReached() =>
        Assert.Equal(IdleStep.Open, new IdleScheduler(300).Check(now: 1000, idle: 300));

    [Fact]
    public void OneAttemptPerIdlePeriod()
    {
        var scheduler = new IdleScheduler(300);
        Assert.Equal(IdleStep.Open, scheduler.Check(now: 1000, idle: 300));
        // Still idle since uptime 700: wait a full timeout, the earliest a new period could fire.
        Assert.Equal(IdleStep.Wait(300), scheduler.Check(now: 1300, idle: 600));
        Assert.Equal(IdleStep.Wait(300), scheduler.Check(now: 1300.2, idle: 600.4));
    }

    [Fact]
    public void NewInputStartsANewPeriod()
    {
        var scheduler = new IdleScheduler(300);
        Assert.Equal(IdleStep.Open, scheduler.Check(now: 1000, idle: 300));
        // Input at uptime 1100, then idle again.
        Assert.Equal(IdleStep.Wait(100), scheduler.Check(now: 1300, idle: 200));
        Assert.Equal(IdleStep.Open, scheduler.Check(now: 1400, idle: 300));
    }

    [Fact]
    public void WaitsForInputAfterTheKeepAwakeLimit()
    {
        // Opened by hotkey at uptime 1000, so the idle trigger never used this idle period.
        var scheduler = new IdleScheduler(3600);
        Assert.Equal(IdleStep.Wait(3600), scheduler.Check(now: 1000, idle: 0));
        // The 30 min limit closes the window; without the rule it would reopen at uptime 4600.
        scheduler.WaitForInput(now: 2800, idle: 1800);
        Assert.Equal(IdleStep.Wait(3600), scheduler.Check(now: 4600, idle: 3600));
        Assert.Equal(IdleStep.Wait(3600), scheduler.Check(now: 9000, idle: 8000));
        // New input at uptime 9100 starts a period that may fire again.
        Assert.Equal(IdleStep.Open, scheduler.Check(now: 12700, idle: 3600));
    }

    [Fact]
    public void ChangingTheTimeoutAppliesAtOnce()
    {
        var scheduler = new IdleScheduler(600);
        Assert.Equal(IdleStep.Wait(200), scheduler.Check(now: 1000, idle: 400));
        scheduler.TimeoutSeconds = 300;
        Assert.Equal(IdleStep.Open, scheduler.Check(now: 1000, idle: 400));
    }

    [Fact]
    public void MinutesToSeconds()
    {
        Assert.Equal(300, IdleTimeoutSetting.Timeout(5));
        Assert.Null(IdleTimeoutSetting.Timeout(0));
        Assert.Null(IdleTimeoutSetting.Timeout(-5));
        Assert.Contains(IdleTimeoutSetting.DefaultMinutes, IdleTimeoutSetting.Choices);
    }

    [Fact]
    public void ReadsTheStoredMinutes()
    {
        var settings = new SettingsStore();
        Assert.Equal(5, IdleTimeoutSetting.Minutes(settings));
        settings.SetInt(IdleTimeoutSetting.Key, 0);
        Assert.Equal(0, IdleTimeoutSetting.Minutes(settings));
        settings.SetString(IdleTimeoutSetting.Key, "ten");
        Assert.Equal(5, IdleTimeoutSetting.Minutes(settings));
    }
}

public class IdleSkipRulesTests
{
    private static readonly HashSet<int> s_ignored = [100, 200];

    private static IdleConditions Conditions(
        bool locked = false,
        bool console = true,
        ShellState shell = ShellState.AcceptsNotifications,
        params AppSound[] sounds) => new(locked, console, shell, sounds);

    [Fact]
    public void OpensWhenNothingBlocks() => Assert.Null(IdleSkipRules.Skip(Conditions(), s_ignored));

    [Fact]
    public void SkipsWhileLocked()
    {
        Assert.Equal(IdleSkipKind.SessionLocked, IdleSkipRules.Skip(Conditions(locked: true), s_ignored)?.Kind);
        // The shell says the same for a screen saver or another user's session in front.
        Assert.Equal(IdleSkipKind.SessionLocked, IdleSkipRules.Skip(Conditions(shell: ShellState.NotPresent), s_ignored)?.Kind);
    }

    [Fact]
    public void SkipsWhenThisSessionIsNotAtTheScreen() =>
        Assert.Equal(IdleSkipKind.NotOnConsole, IdleSkipRules.Skip(Conditions(console: false), s_ignored)?.Kind);

    [Theory]
    [InlineData(ShellState.Busy, IdleSkipKind.Fullscreen)]
    [InlineData(ShellState.Direct3DFullScreen, IdleSkipKind.Fullscreen)]
    [InlineData(ShellState.PresentationMode, IdleSkipKind.Presentation)]
    public void SkipsWhileSomethingIsFullscreenOrPresenting(ShellState shell, IdleSkipKind kind) =>
        Assert.Equal(kind, IdleSkipRules.Skip(Conditions(shell: shell), s_ignored)?.Kind);

    // A Store app in front (Spotify is one), quiet time and an unknown state don't block.
    [Theory]
    [InlineData(ShellState.App)]
    [InlineData(ShellState.QuietTime)]
    [InlineData(ShellState.Unknown)]
    public void OtherShellStatesDoNotBlock(ShellState shell) => Assert.Null(IdleSkipRules.Skip(Conditions(shell: shell), s_ignored));

    [Fact]
    public void SkipsWhileAnotherAppMakesSound()
    {
        var skip = IdleSkipRules.Skip(Conditions(sounds: new AppSound(300, "msedge", 0.2f)), s_ignored);
        Assert.Equal(new IdleSkip(IdleSkipKind.OtherAppPlaying, "msedge"), skip);
        Assert.Equal("msedge is playing sound", IdleSkipRules.Describe(skip!.Value));
    }

    [Fact]
    public void SilenceAndSpotifyDoNotCount()
    {
        var silent = new AppSound(300, "Teams", 0);
        var nearlySilent = new AppSound(301, "chrome", 0.0005f);
        var spotify = new AppSound(100, "Spotify", 0.8f);
        var itself = new AppSound(200, "IdleViz", 0.5f);
        Assert.Null(IdleSkipRules.Skip(Conditions(sounds: [silent, nearlySilent, spotify, itself]), s_ignored));
    }

    [Fact]
    public void LockedComesFirst() =>
        Assert.Equal(
            IdleSkipKind.SessionLocked,
            IdleSkipRules.Skip(Conditions(locked: true, console: false, shell: ShellState.Busy, new AppSound(300, "vlc", 1)), s_ignored)?.Kind);
}
