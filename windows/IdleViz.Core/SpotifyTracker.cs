namespace IdleViz.Core;

/// <summary>
/// Keeps Spotify's current item from the readings of its media session. Windows raises several
/// events for one change, so <see cref="Changed"/> fires only when something other than the
/// position is different, and <see cref="Seeked"/> only when the position jumped.
/// </summary>
public sealed class SpotifyTracker
{
    /// <summary>
    /// A position this far from where the track should have got to by itself is a seek. Spotify's
    /// own reports stay within about a tenth of a second.
    /// </summary>
    public const double SeekThresholdSeconds = 1.5;

    // A track's length is reported a second longer or shorter now and then; a new track's differs by more.
    private const int SameLengthMs = 2000;

    private MediaReading? _reading;

    /// <summary>Whether Spotify has a session in the Windows media controls.</summary>
    public bool HasSession { get; private set; }

    /// <summary>The current item, or null when there is no track.</summary>
    public NowPlaying? Current { get; private set; }

    /// <summary>
    /// Whether <see cref="Current"/> comes from a reading. Until the first one, null means
    /// "don't know yet" rather than "no track".
    /// </summary>
    public bool IsKnown { get; private set; }

    /// <summary>Raised with the new item (null = no track) when the track, its state or its length (by 2 s or more) changed.</summary>
    public event Action<NowPlaying?>? Changed;

    /// <summary>Raised with the current item when someone moved the position within the same track.</summary>
    public event Action<NowPlaying>? Seeked;

    /// <summary>
    /// The current item with its position worked out for <paramref name="now"/>: a playing track has
    /// moved on since the last reading.
    /// </summary>
    public NowPlaying? Snapshot(DateTimeOffset now) => _reading is null ? null : NowPlaying.From(_reading, now);

    /// <summary>A reading of Spotify's session arrived.</summary>
    public void Read(MediaReading reading, DateTimeOffset now)
    {
        HasSession = true;
        // Where the track would be now if nobody touched it.
        var expected = Snapshot(now);
        _reading = reading;
        var item = NowPlaying.From(reading, now);
        Publish(item);
        if (item is not null
            && expected is not null
            && item.Id == expected.Id
            && Math.Abs(item.DurationMs - expected.DurationMs) < SameLengthMs
            && Math.Abs(item.Position - expected.Position) > SeekThresholdSeconds)
        {
            Seeked?.Invoke(item);
        }
    }

    /// <summary>
    /// Reading the session failed. That says nothing about the track, so the last one is kept.
    /// </summary>
    public void ReadFailed() => HasSession = true;

    /// <summary>Spotify has no session: it isn't running, or has played nothing since it started.</summary>
    public void SessionGone()
    {
        HasSession = false;
        _reading = null;
        Publish(null);
    }

    private void Publish(NowPlaying? item)
    {
        var previous = Current;
        var wasKnown = IsKnown;
        Current = item;
        IsKnown = true;
        if (!wasKnown || !SameExceptPosition(previous, item))
        {
            Changed?.Invoke(item);
        }
    }

    // The length counts only when it really differs: see SameLengthMs.
    private static bool SameExceptPosition(NowPlaying? a, NowPlaying? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a with { Position = 0, DurationMs = 0 } == b with { Position = 0, DurationMs = 0 }
            && Math.Abs(a.DurationMs - b.DurationMs) < SameLengthMs;
    }
}
