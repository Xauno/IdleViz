namespace IdleViz.Core.Tests;

public class OpenRulesTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 2, 17, 30, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => s_now + TimeSpan.FromSeconds(seconds);

    // What Spotify reports for about half a second between two tracks.
    private static MediaReading Between() =>
        NowPlayingTests.Song(title: "", artist: "", album: "", position: 0, duration: 0);

    [Fact]
    public void WaitsUntilSomethingIsKnown()
    {
        Assert.Equal(OpenRefusal.NotKnownYet, OpenRules.Refusal(new SpotifyTracker(), s_now));
    }

    [Fact]
    public void RefusesWhenSpotifyIsNotRunning()
    {
        var tracker = new SpotifyTracker();
        tracker.SessionGone();
        Assert.Equal(OpenRefusal.SpotifyNotRunning, OpenRules.Refusal(tracker, s_now));
    }

    // A track seen before Spotify quit doesn't count, not even for a moment.
    [Fact]
    public void ATrackFromBeforeSpotifyQuitDoesNotCount()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.SessionGone();
        Assert.Equal(OpenRefusal.SpotifyNotRunning, OpenRules.Refusal(tracker, At(0.1)));
    }

    [Fact]
    public void RefusesWithoutATrack()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(MediaStatus.Stopped), s_now);
        Assert.Equal(OpenRefusal.NoTrack, OpenRules.Refusal(tracker, s_now));
    }

    [Theory]
    [InlineData(MediaStatus.Playing, "Beach House")]
    [InlineData(MediaStatus.Paused, "Beach House")]
    // A podcast, and an ad, which reads as whatever it looks like.
    [InlineData(MediaStatus.Playing, "")]
    public void OpensForEveryKindOfTrack(MediaStatus status, string artist)
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(status, artist: artist), s_now);
        Assert.Null(OpenRules.Refusal(tracker, s_now));
    }

    // A hotkey press between two songs opens the visualizer.
    [Fact]
    public void TheLastTrackStillCountsForAMomentAfterIt()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(Between(), At(10));
        Assert.Null(OpenRules.Refusal(tracker, At(10)));
        Assert.Null(OpenRules.Refusal(tracker, At(11.4)));
        Assert.Equal(OpenRefusal.NoTrack, OpenRules.Refusal(tracker, At(11.5)));
    }

    // Windows reports the gap two or three times; the first report starts the wait.
    [Fact]
    public void RepeatedReportsOfNoTrackDoNotExtendTheWait()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(Between(), At(10));
        tracker.Read(Between(), At(11));
        Assert.Equal(OpenRefusal.NoTrack, OpenRules.Refusal(tracker, At(11.6)));
    }

    [Fact]
    public void NoTrackFromTheStartHasNoWait()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(Between(), s_now);
        Assert.Equal(OpenRefusal.NoTrack, OpenRules.Refusal(tracker, s_now));
    }

    // The clock was set back: the gap can't be measured, so it doesn't count.
    [Fact]
    public void AGapThatStartsInTheFutureDoesNotCount()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(Between(), At(10));
        Assert.Equal(OpenRefusal.NoTrack, OpenRules.Refusal(tracker, At(9)));
    }

    [Fact]
    public void AFailedReadingKeepsTheTrack()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.ReadFailed();
        Assert.Null(OpenRules.Refusal(tracker, At(60)));
    }

    [Fact]
    public void TheTrackerRemembersWhenTheTrackWentAway()
    {
        var tracker = new SpotifyTracker();
        tracker.Read(NowPlayingTests.Song(), s_now);
        Assert.Null(tracker.TrackLostAt);
        tracker.Read(Between(), At(10));
        Assert.Equal(At(10), tracker.TrackLostAt);
        tracker.Read(NowPlayingTests.Song(title: "Troublemaker"), At(10.5));
        Assert.Null(tracker.TrackLostAt);
        tracker.Read(Between(), At(20));
        tracker.SessionGone();
        Assert.Null(tracker.TrackLostAt);
    }
}
