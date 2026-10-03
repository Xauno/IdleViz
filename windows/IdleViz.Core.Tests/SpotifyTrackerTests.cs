namespace IdleViz.Core.Tests;

public class SpotifyTrackerTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 2, 17, 30, 0, TimeSpan.Zero);

    private static (SpotifyTracker Tracker, List<NowPlaying?> Changes) Make()
    {
        var tracker = new SpotifyTracker();
        var changes = new List<NowPlaying?>();
        tracker.Changed += changes.Add;
        return (tracker, changes);
    }

    [Fact]
    public void KnowsNothingBeforeTheFirstReading()
    {
        var (tracker, changes) = Make();
        Assert.False(tracker.IsKnown);
        Assert.False(tracker.HasSession);
        Assert.Null(tracker.Current);
        Assert.Null(tracker.Snapshot(s_now));
        Assert.Empty(changes);
    }

    [Fact]
    public void AReadingSetsTheTrack()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(), s_now);
        Assert.True(tracker.IsKnown);
        Assert.True(tracker.HasSession);
        Assert.Equal("New Year", tracker.Current?.Title);
        Assert.Equal("New Year", Assert.Single(changes)?.Title);
    }

    // Windows raises two or three events for one change, and a timeline event every few seconds.
    [Fact]
    public void RepeatedReadingsAndNewPositionsAreNotChanges()
    {
        var (tracker, changes) = Make();
        var seeks = 0;
        tracker.Seeked += _ => seeks++;
        tracker.Read(NowPlayingTests.Song(position: 29), s_now);
        tracker.Read(NowPlayingTests.Song(position: 29), s_now);
        // 4.5 s later Spotify reports a position 4.5 s further on.
        tracker.Read(NowPlayingTests.Song(position: 33.5, updatedSecondsAgo: -4.5), s_now + TimeSpan.FromSeconds(4.5));
        Assert.Single(changes);
        Assert.Equal(0, seeks);
        // The position itself is still kept up to date.
        Assert.Equal(33.5, tracker.Current!.Position, 3);
    }

    // Seen with the owner's Spotify: the same song reported as 194.57 s, then 195.3 s, then 194.57 s again.
    [Fact]
    public void ALengthThatWobblesByASecondIsNotAChange()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(duration: 194.57), s_now);
        tracker.Read(NowPlayingTests.Song(duration: 195.3), s_now);
        Assert.Single(changes);
        // The newest length is still the one kept.
        Assert.Equal(195_300, tracker.Current!.DurationMs);
        tracker.Read(NowPlayingTests.Song(duration: 212), s_now);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void PauseResumeAndANewTrackAreChanges()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(NowPlayingTests.Song(MediaStatus.Paused), s_now);
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(NowPlayingTests.Song(title: "Troublemaker", duration: 296), s_now);
        Assert.Equal(
            [SpotifyPlayerState.Playing, SpotifyPlayerState.Paused, SpotifyPlayerState.Playing, SpotifyPlayerState.Playing],
            changes.Select(item => item!.State));
        Assert.Equal("Troublemaker", changes[^1]!.Title);
    }

    [Fact]
    public void TheEmptyMomentBetweenTracksIsNoTrack()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(NowPlayingTests.Song(title: "", artist: "", album: "", position: 0, duration: 0), s_now);
        Assert.Null(tracker.Current);
        Assert.True(tracker.HasSession);
        tracker.Read(NowPlayingTests.Song(title: "Troublemaker"), s_now);
        Assert.Equal(["New Year", null, "Troublemaker"], changes.Select(item => item?.Title));
    }

    [Fact]
    public void AStopIsNoTrack()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(NowPlayingTests.Song(MediaStatus.Stopped), s_now);
        Assert.Null(tracker.Current);
        Assert.Null(changes[^1]);
    }

    [Fact]
    public void AFirstReadingWithNoTrackIsStillNews()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(MediaStatus.Stopped), s_now);
        Assert.True(tracker.IsKnown);
        Assert.Null(Assert.Single(changes));
    }

    [Fact]
    public void AFailedReadingKeepsTheLastTrack()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.ReadFailed();
        Assert.Equal("New Year", tracker.Current?.Title);
        Assert.Single(changes);
    }

    [Fact]
    public void AFailedFirstReadingLeavesTheTrackUnknown()
    {
        var (tracker, changes) = Make();
        tracker.ReadFailed();
        Assert.True(tracker.HasSession);
        Assert.False(tracker.IsKnown);
        Assert.Empty(changes);
    }

    [Fact]
    public void LosingTheSessionClearsTheTrack()
    {
        var (tracker, changes) = Make();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.SessionGone();
        Assert.False(tracker.HasSession);
        Assert.True(tracker.IsKnown);
        Assert.Null(tracker.Current);
        Assert.Null(tracker.Snapshot(s_now));
        Assert.Null(changes[^1]);
        // Still gone: not news again.
        tracker.SessionGone();
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void NoSessionAtStartMeansNoTrackNotUnknown()
    {
        var (tracker, changes) = Make();
        tracker.SessionGone();
        Assert.True(tracker.IsKnown);
        Assert.Null(Assert.Single(changes));
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(5, true)]
    [InlineData(31.6, true)]
    [InlineData(31.4, false)]
    [InlineData(29.5, false)]
    public void AJumpInPositionIsASeek(double newPosition, bool isSeek)
    {
        var (tracker, changes) = Make();
        var seeks = new List<NowPlaying>();
        tracker.Seeked += seeks.Add;
        tracker.Read(NowPlayingTests.Song(position: 29), s_now);
        // One second later the track should be at 30.
        var later = s_now + TimeSpan.FromSeconds(1);
        tracker.Read(NowPlayingTests.Song(position: newPosition, updatedSecondsAgo: -1), later);
        Assert.Equal(isSeek ? 1 : 0, seeks.Count);
        // A seek is not a change of track or state.
        Assert.Single(changes);
        if (isSeek)
        {
            Assert.Equal(newPosition, seeks[0].Position, 3);
        }
    }

    [Fact]
    public void APausedTrackCanBeSeekedToo()
    {
        var (tracker, _) = Make();
        var seeks = 0;
        tracker.Seeked += _ => seeks++;
        tracker.Read(NowPlayingTests.Song(MediaStatus.Paused, position: 29), s_now);
        // A paused track stays put, however long it waits.
        tracker.Read(NowPlayingTests.Song(MediaStatus.Paused, position: 29), s_now + TimeSpan.FromSeconds(40));
        Assert.Equal(0, seeks);
        tracker.Read(NowPlayingTests.Song(MediaStatus.Paused, position: 80), s_now + TimeSpan.FromSeconds(41));
        Assert.Equal(1, seeks);
    }

    // A new track starts at 0, and for a moment Windows may report the new length under the old title.
    [Fact]
    public void ANewTrackIsNotASeek()
    {
        var (tracker, _) = Make();
        var seeks = 0;
        tracker.Seeked += _ => seeks++;
        tracker.Read(NowPlayingTests.Song(position: 200), s_now);
        tracker.Read(NowPlayingTests.Song(position: 0, duration: 212), s_now);
        tracker.Read(NowPlayingTests.Song(title: "Troublemaker", position: 0, duration: 212), s_now);
        Assert.Equal(0, seeks);
    }

    [Fact]
    public void ASnapshotWorksOutThePositionForItsOwnMoment()
    {
        var (tracker, _) = Make();
        tracker.Read(NowPlayingTests.Song(position: 29), s_now);
        Assert.Equal(29, tracker.Snapshot(s_now)!.Position, 3);
        Assert.Equal(32, tracker.Snapshot(s_now + TimeSpan.FromSeconds(3))!.Position, 3);
        // Reading it doesn't change what the tracker holds.
        Assert.Equal(29, tracker.Current!.Position, 3);
    }

    [Fact]
    public void RemembersWhenTheTrackBecameCurrent()
    {
        var (tracker, _) = Make();
        Assert.Null(tracker.CurrentSince);
        tracker.Read(NowPlayingTests.Song(position: 29), s_now);
        Assert.Equal(s_now, tracker.CurrentSince);
        // A pause, a seek and a corrected length are still the same track.
        tracker.Read(NowPlayingTests.Song(MediaStatus.Paused, position: 30), s_now.AddSeconds(1));
        tracker.Read(NowPlayingTests.Song(MediaStatus.Paused, position: 90), s_now.AddSeconds(2));
        tracker.Read(NowPlayingTests.Song(MediaStatus.Paused, position: 90, duration: 330), s_now.AddSeconds(3));
        Assert.Equal(s_now, tracker.CurrentSince);
        tracker.Read(NowPlayingTests.Song(title: "Troublemaker", position: 0), s_now.AddSeconds(4));
        Assert.Equal(s_now.AddSeconds(4), tracker.CurrentSince);
    }

    [Fact]
    public void NoTrackHasNoTimeAndTheSameTrackAfterAGapStartsOver()
    {
        var (tracker, _) = Make();
        tracker.Read(NowPlayingTests.Song(), s_now);
        tracker.Read(NowPlayingTests.Song(title: ""), s_now.AddSeconds(1));
        Assert.Null(tracker.CurrentSince);
        tracker.Read(NowPlayingTests.Song(), s_now.AddSeconds(2));
        Assert.Equal(s_now.AddSeconds(2), tracker.CurrentSince);
        tracker.SessionGone();
        Assert.Null(tracker.CurrentSince);
    }
}
