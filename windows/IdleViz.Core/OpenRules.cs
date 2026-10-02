namespace IdleViz.Core;

/// <summary>Why the visualizer didn't open.</summary>
public enum OpenRefusal
{
    /// <summary>Spotify has no session in the media controls: it isn't running, or has played nothing since it started.</summary>
    SpotifyNotRunning,

    /// <summary>Spotify is running but has no current track.</summary>
    NoTrack,

    /// <summary>No reading of the media controls has arrived yet, so neither of the above is known.</summary>
    NotKnownYet,
}

/// <summary>
/// The visualizer only opens while Spotify is running and has a current track, playing or paused.
/// Songs, podcasts and ads all count; the overlay decides what to show for them.
/// </summary>
public static class OpenRules
{
    /// <summary>
    /// How long a track still counts after Spotify stopped reporting one, while Spotify keeps running.
    /// Spotify reports no track for about half a second at every track change. The page waits as
    /// long before it hides the overlay.
    /// </summary>
    public const double TrackGraceSeconds = 1.5;

    /// <summary>
    /// How long a trigger waits for the first reading (the app has only just started) before it is refused.
    /// </summary>
    public const double FirstReadingWaitSeconds = 2;

    /// <summary>Returns null when the visualizer may open.</summary>
    public static OpenRefusal? Refusal(SpotifyTracker tracker, DateTimeOffset now)
    {
        if (!tracker.IsKnown)
        {
            return OpenRefusal.NotKnownYet;
        }

        // A track seen before Spotify quit doesn't count.
        if (!tracker.HasSession)
        {
            return OpenRefusal.SpotifyNotRunning;
        }

        if (tracker.Current is not null)
        {
            return null;
        }

        return tracker.TrackLostAt is { } lost && (now - lost).TotalSeconds is >= 0 and < TrackGraceSeconds
            ? null
            : OpenRefusal.NoTrack;
    }
}
