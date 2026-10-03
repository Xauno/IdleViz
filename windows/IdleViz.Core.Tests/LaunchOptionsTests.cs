namespace IdleViz.Core.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void NoArgumentsAsksForNothing()
    {
        Assert.Equal(new LaunchOptions(), LaunchOptions.Parse([]));
    }

    [Fact]
    public void ReadsTheUrl()
    {
        Assert.Equal(UrlCommand.Open, LaunchOptions.Parse(["idleviz://open"]).Command);
        Assert.Equal(UrlCommand.Open, LaunchOptions.Parse(["idleviz://open/"]).Command);
        Assert.Null(LaunchOptions.Parse(["idleviz://close"]).Command);
        Assert.Null(LaunchOptions.Parse(["open"]).Command);
    }

    [Fact]
    public void ReadsTheDebugSwitches()
    {
        var options = LaunchOptions.Parse(["--no-dismiss", "--SHOW-SETTINGS", "--open-at-launch", "--show-flyout", "--show-menu", "--hang-page"]);
        Assert.True(options.ShowFlyout);
        Assert.True(options.ShowMenu);
        Assert.True(options.NoDismiss);
        Assert.True(options.ShowSettings);
        Assert.True(options.OpenAtLaunch);
        Assert.True(options.HangPage);
        Assert.Null(options.Command);
    }

    [Fact]
    public void UnknownArgumentsAreIgnored()
    {
        Assert.Equal(new LaunchOptions(), LaunchOptions.Parse(["--verbose", @"C:\some\file.txt", ""]));
    }

    [Fact]
    public void SplitsACommandLine()
    {
        Assert.Equal(
            [@"C:\Program Files\IdleViz\IdleViz.exe", "idleviz://open", "--no-dismiss"],
            LaunchOptions.SplitCommandLine("""
                "C:\Program Files\IdleViz\IdleViz.exe" "idleviz://open"   --no-dismiss
                """));
        Assert.Empty(LaunchOptions.SplitCommandLine(null));
        Assert.Empty(LaunchOptions.SplitCommandLine("   "));
        Assert.Equal(["", "a"], LaunchOptions.SplitCommandLine("\"\" a"));
    }
}
