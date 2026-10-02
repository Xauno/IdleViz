namespace IdleViz.Core.Tests;

public class NowPlayingTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 2, 17, 30, 0, TimeSpan.Zero);

    // What Spotify reported for a song during the W2 spike.
    internal static MediaReading Song(
        MediaStatus status = MediaStatus.Playing,
        string title = "New Year",
        string artist = "Beach House",
        string album = "Bloom",
        double position = 29.03,
        double duration = 325.76,
        double? updatedSecondsAgo = 0) =>
        new(
            status,
            title,
            artist,
            album,
            TimeSpan.FromSeconds(position),
            TimeSpan.FromSeconds(duration),
            updatedSecondsAgo is { } ago ? s_now - TimeSpan.FromSeconds(ago) : null);

    internal static NowPlaying? Read(MediaReading reading) => NowPlaying.From(reading, s_now);

    [Fact]
    public void ReadsASong()
    {
        var item = Read(Song());
        Assert.NotNull(item);
        Assert.Equal(SpotifyPlayerState.Playing, item.State);
        Assert.Equal(SpotifyContent.Song, item.Content);
        Assert.Equal("New Year", item.Title);
        Assert.Equal("Beach House", item.Artist);
        Assert.Equal("Bloom", item.Album);
        Assert.Equal(325_760, item.DurationMs);
        Assert.Equal(29.03, item.Position, 3);
    }

    [Fact]
    public void ReadsAPausedSong()
    {
        Assert.Equal(SpotifyPlayerState.Paused, Read(Song(MediaStatus.Paused))?.State);
    }

    // The episode the owner played during the spike: a title, the show as the album, no artist.
    [Fact]
    public void AnEmptyArtistIsAPodcast()
    {
        var item = Read(Song(title: "The Biggest Losers In Financial Audit History", artist: "", album: "Financial Audit", duration: 5850.28));
        Assert.Equal(SpotifyContent.Podcast, item?.Content);
        Assert.Equal(5_850_280, item?.DurationMs);
    }

    [Theory]
    [InlineData(MediaStatus.Closed)]
    [InlineData(MediaStatus.Opened)]
    [InlineData(MediaStatus.Changing)]
    [InlineData(MediaStatus.Stopped)]
    public void OnlyPlayingAndPausedHaveATrack(MediaStatus status)
    {
        Assert.Null(Read(Song(status)));
    }

    // Between two tracks Spotify reports "Playing" with every text empty and a duration of 0, for about half a second.
    [Fact]
    public void AnEmptyTitleIsNoTrack()
    {
        Assert.Null(Read(Song(title: "", artist: "", album: "", position: 0, duration: 0)));
    }

    [Fact]
    public void APlayingTrackHasMovedOnSinceItsPositionWasReported()
    {
        Assert.Equal(33.43, Read(Song(position: 29.03, updatedSecondsAgo: 4.4))!.Position, 3);
    }

    [Fact]
    public void APausedTrackStaysWhereItWasReported()
    {
        Assert.Equal(37.11, Read(Song(MediaStatus.Paused, position: 37.11, updatedSecondsAgo: 30))!.Position, 3);
    }

    [Fact]
    public void ThePositionNeverPassesTheEnd()
    {
        Assert.Equal(325.76, Read(Song(position: 324, updatedSecondsAgo: 5))!.Position, 3);
    }

    [Fact]
    public void APositionWithNoReportTimeIsUsedAsItIs()
    {
        Assert.Equal(29.03, Read(Song(updatedSecondsAgo: null))!.Position, 3);
    }

    // A report from the future (the clock was set back) or a very old one adds nothing.
    [Theory]
    [InlineData(-5)]
    [InlineData(60)]
    [InlineData(3600)]
    public void AReportTimeThatCannotBeTrustedAddsNothing(double updatedSecondsAgo)
    {
        Assert.Equal(29.03, Read(Song(updatedSecondsAgo: updatedSecondsAgo))!.Position, 3);
    }

    [Fact]
    public void NegativeTimesReadAsZero()
    {
        var item = Read(Song(position: -3, duration: -1));
        Assert.Equal(0, item!.Position);
        Assert.Equal(0, item.DurationMs);
    }

    [Fact]
    public void TheIdTellsTracksApartByTitleArtistAndAlbum()
    {
        var id = Read(Song())!.Id;
        Assert.Equal(id, Read(Song(MediaStatus.Paused, position: 100))!.Id);
        Assert.NotEqual(id, Read(Song(title: "Troublemaker"))!.Id);
        Assert.NotEqual(id, Read(Song(artist: "Someone Else"))!.Id);
        Assert.NotEqual(id, Read(Song(album: "Bloom (Live)"))!.Id);
        // The same letters split differently between the fields are different tracks.
        Assert.NotEqual(Read(Song(title: "AB", artist: "C"))!.Id, Read(Song(title: "A", artist: "BC"))!.Id);
    }
}

public class SpotifySessionTests
{
    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    [InlineData("spotifyab.spotifymusic_zpdnekdrzrea0!Spotify")]
    [InlineData("Spotify.exe")]
    [InlineData("SPOTIFY.EXE")]
    public void RecognisesBothSpotifyVersions(string id)
    {
        Assert.True(SpotifySession.IsSpotify(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chrome.exe")]
    [InlineData("MSEdge")]
    [InlineData("NotSpotify.exe")]
    [InlineData("Spotify.exe.fake")]
    [InlineData("SomeoneElse.SpotifyMusic_abc!App")]
    public void IgnoresEveryOtherPlayer(string? id)
    {
        Assert.False(SpotifySession.IsSpotify(id));
    }
}
