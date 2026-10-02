using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace IdleViz.App;

/// <summary>
/// Reads what Spotify is playing from the Windows media controls, without ever launching it and
/// without a Spotify login. Only Spotify's own session is read, never another player's. It is
/// driven by the session's events; nothing is polled.
/// </summary>
internal sealed class SpotifyInfo : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly ArtworkCache _artwork = new();
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> _sessionsChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _propertiesChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _playbackChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _timelineChanged;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _reading;
    private bool _readAgain;
    private bool _failureLogged;
    private bool _disposed;

    public SpotifyInfo(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        // The events arrive on other threads; everything here runs on the UI thread.
        _sessionsChanged = (_, _) => _dispatcher.TryEnqueue(FindSession);
        _propertiesChanged = (_, _) => _dispatcher.TryEnqueue(Refresh);
        _playbackChanged = (_, _) => _dispatcher.TryEnqueue(Refresh);
        _timelineChanged = (_, _) => _dispatcher.TryEnqueue(Refresh);
        Tracker.Changed += LogChange;
        Tracker.Seeked += LogSeek;
    }

    public SpotifyTracker Tracker { get; } = new();

    /// <summary>Raised when the cover of the current track arrived or changed.</summary>
    public event Action? ArtworkChanged;

    /// <summary>The cover of a track as JPEG, PNG or WebP bytes, or null if Spotify gave none.</summary>
    public byte[]? ArtworkFor(NowPlaying item) => _artwork.Image(item.Id);

    public async void Start()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
            {
                return;
            }

            _manager.SessionsChanged += _sessionsChanged;
            FindSession();
        }
        catch (Exception error)
        {
            Log.Info("spotify", $"Can't read what Spotify is playing: the media controls are not available. {error.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Tracker.Changed -= LogChange;
        Tracker.Seeked -= LogSeek;
        if (_manager is not null)
        {
            _manager.SessionsChanged -= _sessionsChanged;
        }

        Follow(null);
    }

    // Spotify's session appears when it first plays something and goes when it quits.
    private void FindSession()
    {
        if (_disposed || _manager is null)
        {
            return;
        }

        GlobalSystemMediaTransportControlsSession? session;
        try
        {
            session = _manager.GetSessions().FirstOrDefault(s => SpotifySession.IsSpotify(s.SourceAppUserModelId));
        }
        catch (Exception error)
        {
            LogFailure(error);
            return;
        }

        var had = _session is not null;
        Follow(session);
        if (session is null)
        {
            if (had || !Tracker.IsKnown)
            {
                Log.Info("spotify", "Spotify has no media session (not running, or nothing played yet)");
            }

            Tracker.SessionGone();
            return;
        }

        if (!had)
        {
            Log.Info("spotify", $"Following the session {session.SourceAppUserModelId}");
        }

        Refresh();
    }

    private void Follow(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= _propertiesChanged;
            _session.PlaybackInfoChanged -= _playbackChanged;
            _session.TimelinePropertiesChanged -= _timelineChanged;
        }

        _session = session;
        if (session is not null)
        {
            session.MediaPropertiesChanged += _propertiesChanged;
            session.PlaybackInfoChanged += _playbackChanged;
            session.TimelinePropertiesChanged += _timelineChanged;
        }
    }

    // One reading at a time. Events that arrive during a reading are merged into one more reading after it.
    private async void Refresh()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }

        _reading = true;
        try
        {
            do
            {
                _readAgain = false;
                var session = _session;
                if (session is null || _disposed)
                {
                    return;
                }

                try
                {
                    var properties = await session.TryGetMediaPropertiesAsync();
                    // Spotify may have quit, or its session been replaced, while that ran.
                    if (session != _session)
                    {
                        continue;
                    }

                    // A session that is closing has nothing left to read. Its end is reported separately.
                    if (properties is null)
                    {
                        Tracker.ReadFailed();
                        continue;
                    }

                    var playback = session.GetPlaybackInfo();
                    var timeline = session.GetTimelineProperties();
                    Tracker.Read(
                        new MediaReading(
                            Status(playback.PlaybackStatus),
                            properties.Title ?? string.Empty,
                            properties.Artist ?? string.Empty,
                            properties.AlbumTitle ?? string.Empty,
                            timeline.Position,
                            timeline.EndTime - timeline.StartTime,
                            // Windows reports the year 1601 when Spotify hasn't given a position yet.
                            timeline.LastUpdatedTime.Year > 2000 ? timeline.LastUpdatedTime : null),
                        DateTimeOffset.Now);
                    _failureLogged = false;
                    if (Tracker.Current is { } item)
                    {
                        await ReadArtwork(properties.Thumbnail, item, session);
                    }
                }
                catch (Exception error)
                {
                    Tracker.ReadFailed();
                    LogFailure(error);
                }
            }
            while (_readAgain);
        }
        finally
        {
            _reading = false;
        }
    }

    private async Task ReadArtwork(IRandomAccessStreamReference? thumbnail, NowPlaying item, GlobalSystemMediaTransportControlsSession session)
    {
        if (thumbnail is null)
        {
            return;
        }

        using var stream = await thumbnail.OpenReadAsync();
        if (stream.Size is 0 or > Artwork.MaxBytes)
        {
            return;
        }

        var bytes = new byte[(int)stream.Size];
        var read = await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        // Not an image the page may show, or the track moved on while the cover was read.
        if (read.Length != bytes.Length || Artwork.MimeType(bytes) is not { } type || session != _session || Tracker.Current?.Id != item.Id)
        {
            return;
        }

        // Spotify can hand over a new track with the old cover for a moment, so a later cover replaces the first.
        if (_artwork.Image(item.Id) is { } known && known.AsSpan().SequenceEqual(bytes))
        {
            return;
        }

        _artwork.Insert(bytes, item.Id);
        Log.Info("spotify", string.Create(CultureInfo.InvariantCulture, $"Artwork for \"{item.Title}\": {type}, {bytes.Length / 1024} KB"));
        ArtworkChanged?.Invoke();
    }

    private static MediaStatus Status(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaStatus.Opened,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaStatus.Changing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaStatus.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaStatus.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaStatus.Paused,
        _ => MediaStatus.Closed,
    };

    private static void LogChange(NowPlaying? item)
    {
        if (item is null)
        {
            Log.Info("spotify", "No track");
            return;
        }

        var by = item.Content == SpotifyContent.Podcast ? $"from \"{item.Album}\"" : $"by {item.Artist}, on \"{item.Album}\"";
        Log.Info("spotify", $"{item.Content}, {item.State}: \"{item.Title}\" {by}, at {Clock(item.Position)} of {Clock(item.DurationMs / 1000.0)}");
    }

    private static void LogSeek(NowPlaying item) =>
        Log.Info("spotify", $"Seek in \"{item.Title}\" to {Clock(item.Position)}");

    private static string Clock(double seconds)
    {
        var whole = (int)Math.Floor(seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }

    // A failure repeats with every event until it clears, so it is logged once.
    private void LogFailure(Exception error)
    {
        if (!_failureLogged)
        {
            _failureLogged = true;
            Log.Info("spotify", $"Can't read what Spotify is playing; keeping the last track. {error.Message}");
        }
    }
}
