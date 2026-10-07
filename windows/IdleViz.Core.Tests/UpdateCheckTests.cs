namespace IdleViz.Core.Tests;

public class UpdateCheckTests
{
    private static readonly DateTimeOffset s_now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void ReadsATagAndAPlainVersion()
    {
        Assert.Equal(new Version(0, 1, 2, 0), UpdateCheck.ParseVersion("v0.1.2"));
        Assert.Equal(new Version(0, 1, 2, 0), UpdateCheck.ParseVersion("0.1.2"));
        Assert.Equal(new Version(2, 0, 0, 0), UpdateCheck.ParseVersion(" V2.0.0\n"));
    }

    [Fact]
    public void MissingNumbersAreZero()
    {
        Assert.Equal(UpdateCheck.ParseVersion("0.1.0"), UpdateCheck.ParseVersion("0.1"));
        Assert.Equal(UpdateCheck.ParseVersion("0.1.1"), UpdateCheck.ParseVersion("0.1.1.0"));
        Assert.Equal("1.0.0", UpdateCheck.Label(UpdateCheck.ParseVersion("1")!));
        Assert.Equal("0.1.1", UpdateCheck.Label(new Version(0, 1, 1, 0)));
        Assert.Equal("0.1.0", UpdateCheck.Label(new Version(0, 1)));
        Assert.Equal("0.1.1.4", UpdateCheck.Label(new Version(0, 1, 1, 4)));
    }

    [Fact]
    public void ComparesNumbersNotText()
    {
        Assert.True(UpdateCheck.ParseVersion("0.1.9") < UpdateCheck.ParseVersion("0.1.10"));
        Assert.True(UpdateCheck.ParseVersion("0.9.9") < UpdateCheck.ParseVersion("1.0"));
        Assert.True(UpdateCheck.ParseVersion("0.1") < UpdateCheck.ParseVersion("0.1.0.1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("0.2.0-beta")]
    [InlineData("0.2.0 beta")]
    [InlineData("1..2")]
    [InlineData(".1")]
    [InlineData("1.")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.0")]
    [InlineData("1.٢")]
    [InlineData("99999999999.0")]
    public void AnythingElseIsNotAVersion(string? text)
    {
        Assert.Null(UpdateCheck.ParseVersion(text));
    }

    [Fact]
    public void TheAssemblyVersionIsFilledIn()
    {
        Assert.Equal(new Version(0, 1, 1, 0), UpdateCheck.Installed(new Version(0, 1, 1)));
        Assert.Equal(new Version(0, 1, 1, 0), UpdateCheck.Installed(new Version(0, 1, 1, 0)));
        Assert.Null(UpdateCheck.Installed(null));
    }

    [Fact]
    public void IsOnUnlessSwitchedOff()
    {
        var settings = new SettingsStore();
        Assert.True(UpdateCheck.IsEnabled(settings));
        settings.SetBool(UpdateCheck.EnabledKey, false);
        Assert.False(UpdateCheck.IsEnabled(settings));
        settings.SetString(UpdateCheck.EnabledKey, "no");
        Assert.True(UpdateCheck.IsEnabled(settings));
    }

    [Fact]
    public void ReadsTheTagOfTheLatestRelease()
    {
        const string Answer = """{"tag_name": "v0.1.2", "name": "IdleViz v0.1.2", "draft": false, "prerelease": false, "assets": []}""";
        Assert.Equal(new Version(0, 1, 2, 0), UpdateCheck.LatestVersion(Answer));
        Assert.Equal(new Version(3, 0, 0, 0), UpdateCheck.LatestVersion("""{"tag_name": "v3.0.0"}"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"message": "API rate limit exceeded"}""")]
    [InlineData("""{"tag_name": 12}""")]
    [InlineData("""{"tag_name": "nightly"}""")]
    [InlineData("""{"tag_name": "v0.2.0", "prerelease": true}""")]
    [InlineData("""{"tag_name": "v0.2.0", "draft": true}""")]
    public void AnAnswerThatIsNotAReleaseGivesNothing(string? answer)
    {
        Assert.Null(UpdateCheck.LatestVersion(answer));
    }

    [Fact]
    public void ChecksOnceADay()
    {
        Assert.Equal(TimeSpan.Zero, UpdateCheck.Wait(null, s_now));
        Assert.Equal(UpdateCheck.Interval, UpdateCheck.Wait(s_now, s_now));
        Assert.Equal(TimeSpan.FromHours(23), UpdateCheck.Wait(s_now.AddHours(-1), s_now));
        Assert.Equal(TimeSpan.Zero, UpdateCheck.Wait(s_now - UpdateCheck.Interval, s_now));
        Assert.Equal(TimeSpan.Zero, UpdateCheck.Wait(s_now.AddDays(-7), s_now));
    }

    [Fact]
    public void ALastCheckInTheFutureCountsAsNever()
    {
        Assert.Equal(TimeSpan.Zero, UpdateCheck.Wait(s_now.AddMinutes(1), s_now));
    }

    [Fact]
    public void OffersOnlyANewerRelease()
    {
        var installed = UpdateCheck.ParseVersion("0.1.1");
        Assert.Equal(new Version(0, 1, 2, 0), UpdateCheck.Available(installed, UpdateCheck.ParseVersion("0.1.2")));
        Assert.Null(UpdateCheck.Available(installed, UpdateCheck.ParseVersion("0.1.1")));
        Assert.Null(UpdateCheck.Available(installed, UpdateCheck.ParseVersion("0.1.0")));
        Assert.Null(UpdateCheck.Available(installed, null));
        Assert.Null(UpdateCheck.Available(null, UpdateCheck.ParseVersion("9.0")));
    }

    [Fact]
    public void RemembersWhatGitHubAnswered()
    {
        var settings = new SettingsStore();
        Assert.Null(UpdateCheck.LastCheck(settings));

        UpdateCheck.Store(settings, new Version(0, 1, 2, 0), s_now);
        Assert.Equal(s_now, UpdateCheck.LastCheck(settings));
        Assert.Equal("0.1.2", settings.GetString(UpdateCheck.LatestKey));
        Assert.Equal(new Version(0, 1, 2, 0), UpdateCheck.ParseVersion(settings.GetString(UpdateCheck.LatestKey)));
    }

    [Fact]
    public void AStoredTimeThatIsNoDateCountsAsNever()
    {
        var settings = new SettingsStore();
        settings.SetDouble(UpdateCheck.LastCheckKey, -5);
        Assert.Null(UpdateCheck.LastCheck(settings));
        settings.SetDouble(UpdateCheck.LastCheckKey, 1e300);
        Assert.Null(UpdateCheck.LastCheck(settings));
        settings.SetString(UpdateCheck.LastCheckKey, "yesterday");
        Assert.Null(UpdateCheck.LastCheck(settings));
    }
}
