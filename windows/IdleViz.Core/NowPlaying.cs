namespace IdleViz.Core;

/// <summary>
/// What the overlay should treat the current item as. Windows doesn't say what an item is, so it is
/// read from the artist: a podcast episode has none. Ads are not recognised on Windows (the Mac splits
/// them into music ads and podcast ads); an ad is treated as whatever it looks like.
/// </summary>
public enum SpotifyContent
{
    Song,
    Podcast,
}

public enum SpotifyPlayerState
{
    Playing,
    Paused,
}

/// <summary>The playback status of a session in the Windows media controls.</summary>
public enum MediaStatus
{
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// One reading of Spotify's session in the Windows media controls, as Windows reports it.
/// <c>Position</c> is where playback was at <c>PositionUpdated</c> (null if Spotify never reported one), not now.
/// <c>Duration</c> is the end of the timeline, and is zero for a moment while Spotify switches tracks.
/// </summary>
public sealed record MediaReading(
    MediaStatus Status,
    string Title,
    string Artist,
    string Album,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset? PositionUpdated);

/// <summary>
/// Spotify's current item. <c>Id</c> stands in for a track ID, which Windows doesn't give: title,
/// artist and album together. <c>Position</c> is in seconds, at the moment of the reading.
/// </summary>
public sealed record NowPlaying(
    string Id,
    SpotifyPlayerState State,
    SpotifyContent Content,
    string Title,
    string Artist,
    string Album,
    int DurationMs,
    double Position)
{
    // Unit separator: it can't appear in a title, so "A" + "BC" and "AB" + "C" stay different tracks.
    private const char Separator = '\u001F';

    /// <summary>
    /// Reads a session, or returns null when there is no current track: playback stopped or closed,
    /// or an empty title (Spotify reports one for a moment between tracks).
    /// </summary>
    /// <param name="reading">What Windows reported.</param>
    /// <param name="now">The time of the reading, to work out where a playing track has got to.</param>
    public static NowPlaying? From(MediaReading reading, DateTimeOffset now)
    {
        SpotifyPlayerState state;
        switch (reading.Status)
        {
            case MediaStatus.Playing:
                state = SpotifyPlayerState.Playing;
                break;
            case MediaStatus.Paused:
                state = SpotifyPlayerState.Paused;
                break;
            default:
                return null;
        }

        if (string.IsNullOrEmpty(reading.Title))
        {
            return null;
        }

        var duration = Math.Max(0, reading.Duration.TotalSeconds);
        return new NowPlaying(
            Id: string.Join(Separator, reading.Title, reading.Artist, reading.Album),
            State: state,
            Content: string.IsNullOrEmpty(reading.Artist) ? SpotifyContent.Podcast : SpotifyContent.Song,
            Title: reading.Title,
            Artist: reading.Artist,
            Album: reading.Album,
            DurationMs: (int)Math.Round(Math.Min(duration, int.MaxValue / 1000) * 1000),
            Position: PositionAt(reading, state, now, duration));
    }

    // Spotify reports the position only every few seconds, with the time it was true. While playing,
    // the time since then is added, so a reading taken between two reports isn't seconds behind.
    private static double PositionAt(MediaReading reading, SpotifyPlayerState state, DateTimeOffset now, double duration)
    {
        var position = Math.Max(0, reading.Position.TotalSeconds);
        if (state == SpotifyPlayerState.Playing && reading.PositionUpdated is { } updated)
        {
            var elapsed = (now - updated).TotalSeconds;
            // A clock that was set back, or a report so old it can't be trusted, adds nothing.
            if (elapsed is > 0 and < MaxPositionAgeSeconds)
            {
                position += elapsed;
            }
        }

        return duration > 0 ? Math.Min(position, duration) : position;
    }

    /// <summary>Spotify reports its position about every 4.5 s while playing. Far older than that, and the report isn't about this playback.</summary>
    public const double MaxPositionAgeSeconds = 60;
}

/// <summary>Tells Spotify's session from every other player's. No other session is ever read.</summary>
public static class SpotifySession
{
    /// <summary>
    /// The Microsoft Store version is "SpotifyAB.SpotifyMusic_…!Spotify". The version from spotify.com is
    /// taken to be "Spotify.exe", the way Windows names an app that isn't from the Store; that one is untested.
    /// </summary>
    public static bool IsSpotify(string? sourceAppUserModelId)
    {
        if (string.IsNullOrEmpty(sourceAppUserModelId))
        {
            return false;
        }

        return sourceAppUserModelId.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase)
            || sourceAppUserModelId.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase);
    }
}
