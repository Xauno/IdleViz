using IdleViz.Core;
using Microsoft.UI.Dispatching;

namespace IdleViz.App;

/// <summary>
/// Keeps the page's overlay up to date: on every Spotify change, when a cover arrives, when the
/// window opens, and every 5 s while it is open, so the progress bar never drifts far.
/// </summary>
internal sealed class OverlayFeed
{
    private readonly SpotifyInfo _spotify;
    private readonly PageView _page;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _artworkTimer;

    public OverlayFeed(SpotifyInfo spotify, PageView page, DispatcherQueue dispatcher)
    {
        _spotify = spotify;
        _page = page;
        _refreshTimer = dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += (_, _) => Send();
        // When a new track's cover hasn't come, the page is told again once the wait is over, so it shows the note.
        _artworkTimer = dispatcher.CreateTimer();
        _artworkTimer.IsRepeating = false;
        _artworkTimer.Tick += (_, _) => Send();
        spotify.Tracker.Changed += _ => Send();
        spotify.Tracker.Seeked += _ => Send();
        spotify.ArtworkChanged += Send;
    }

    /// <summary>The window opened: send now, and every 5 s until it closes.</summary>
    public void Start()
    {
        Send();
        _refreshTimer.Start();
    }

    public void Stop() => _refreshTimer.Stop();

    private void Send()
    {
        var now = DateTimeOffset.Now;
        var tracker = _spotify.Tracker;
        var item = tracker.Snapshot(now);
        if (tracker.Current is null || item is null)
        {
            _artworkTimer.Stop();
            _page.Show(null);
            return;
        }

        var payload = OverlayPayload.From(item, _spotify.ArtworkFor(item), tracker.CurrentSince, now);
        _page.Show(payload);
        _artworkTimer.Stop();
        if (payload.ArtworkPending && tracker.CurrentSince is { } since)
        {
            var left = since.AddSeconds(OverlayPayload.ArtworkWaitSeconds) - now;
            _artworkTimer.Interval = left > TimeSpan.Zero ? left : TimeSpan.Zero;
            _artworkTimer.Start();
        }
    }
}
