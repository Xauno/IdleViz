namespace IdleViz.Core.Tests;

public class TriggerTests
{
    [Fact]
    public void ParsesOpen()
    {
        Assert.Equal(UrlCommand.Open, UrlCommands.Parse("idleviz://open"));
        Assert.Equal(UrlCommand.Open, UrlCommands.Parse("IDLEVIZ://OPEN"));
        Assert.Equal(UrlCommand.Open, UrlCommands.Parse("idleviz://open/"));
    }

    [Fact]
    public void RejectsUnknownCommands()
    {
        Assert.Null(UrlCommands.Parse("idleviz://close"));
        Assert.Null(UrlCommands.Parse("idleviz://"));
    }

    [Fact]
    public void RejectsOtherSchemes()
    {
        Assert.Null(UrlCommands.Parse("idleviz-app://open"));
        Assert.Null(UrlCommands.Parse("https://open"));
    }

    // Windows passes whatever follows the exe on the command line, so it may not be a URL at all.
    [Fact]
    public void RejectsTextThatIsNotAUrl()
    {
        Assert.Null(UrlCommands.Parse(null));
        Assert.Null(UrlCommands.Parse(""));
        Assert.Null(UrlCommands.Parse("open"));
        Assert.Null(UrlCommands.Parse(@"C:\Users\someone\open"));
    }
}

public class TriggerSourceTests
{
    [Fact]
    public void OnlyIdleIsAutomatic()
    {
        Assert.True(TriggerSource.Hotkey.IsManual());
        Assert.True(TriggerSource.UrlScheme.IsManual());
        Assert.True(TriggerSource.Settings.IsManual());
        Assert.False(TriggerSource.Idle.IsManual());
    }
}
