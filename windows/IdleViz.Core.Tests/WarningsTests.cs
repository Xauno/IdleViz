namespace IdleViz.Core.Tests;

public class WarningsTests
{
    [Fact]
    public void StartsWithNoProblems()
    {
        var warnings = new WarningList();
        Assert.False(warnings.Any);
        Assert.Empty(warnings.Current);
        Assert.Equal("IdleViz", warnings.CurrentTooltip);
    }

    [Fact]
    public void AProblemShowsUntilItsCheckPasses()
    {
        var warnings = new WarningList();
        var changes = 0;
        warnings.Changed += () => changes++;

        warnings.Report(WarningKind.AudioCapture, "Access is denied.");
        Assert.True(warnings.Any);
        Assert.Equal("IdleViz, something needs attention", warnings.CurrentTooltip);
        var warning = Assert.Single(warnings.Current);
        Assert.Equal("Spotify audio can't be captured", warning.Title);
        Assert.Equal("Access is denied.", warning.Details);
        Assert.Equal(1, changes);

        warnings.Report(WarningKind.AudioCapture, null);
        Assert.False(warnings.Any);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void ARepeatedResultIsAnnouncedOnce()
    {
        var warnings = new WarningList();
        var changes = 0;
        warnings.Changed += () => changes++;

        warnings.Report(WarningKind.NowPlaying, null);
        Assert.Equal(0, changes);
        warnings.Report(WarningKind.NowPlaying, "The server is unavailable.");
        warnings.Report(WarningKind.NowPlaying, "The server is unavailable.");
        warnings.Report(WarningKind.NowPlaying, " The server is unavailable.\r\n");
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ANewErrorTextReplacesTheOld()
    {
        var warnings = new WarningList();
        var changes = 0;
        warnings.Changed += () => changes++;

        warnings.Report(WarningKind.AudioCapture, "First.");
        warnings.Report(WarningKind.AudioCapture, "Second.");
        Assert.Equal("Second.", Assert.Single(warnings.Current).Details);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void EachProblemHasItsOwnRowInAFixedOrder()
    {
        var warnings = new WarningList();
        warnings.Report(WarningKind.NowPlaying, "B");
        warnings.Report(WarningKind.AudioCapture, "A");
        Assert.Equal([WarningKind.AudioCapture, WarningKind.NowPlaying], warnings.Current.Select(w => w.Kind));
        Assert.Equal("Can't read what Spotify is playing", warnings.Current[1].Title);

        warnings.Report(WarningKind.AudioCapture, null);
        Assert.Equal(WarningKind.NowPlaying, Assert.Single(warnings.Current).Kind);
        Assert.True(warnings.Any);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AnEmptyErrorTextStillCounts(string problem)
    {
        var warnings = new WarningList();
        warnings.Report(WarningKind.AudioCapture, problem);
        Assert.Equal("No details were given.", Assert.Single(warnings.Current).Details);
    }

    [Theory]
    [InlineData(WarningKind.AudioCapture)]
    [InlineData(WarningKind.NowPlaying)]
    public void EveryProblemExplainsItself(WarningKind kind)
    {
        var warning = new Warning(kind, "x");
        Assert.NotEmpty(warning.Title);
        Assert.NotEmpty(warning.Explanation);
    }
}
