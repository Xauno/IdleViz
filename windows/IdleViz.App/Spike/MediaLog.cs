#pragma warning disable
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace IdleViz.App.Spike;

/// <summary>SPIKE: logs everything the Windows media controls report, for every session.</summary>
internal sealed class MediaLog
{
    private GlobalSystemMediaTransportControlsSessionManager _manager = null!;
    private readonly HashSet<string> _hooked = [];

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => Sessions("SessionsChanged");
        _manager.CurrentSessionChanged += (_, _) => Log.Info("media", $"CurrentSessionChanged: {_manager.GetCurrentSession()?.SourceAppUserModelId ?? "(none)"}");
        Sessions("start");
    }

    /// <summary>Logs the position of every session now, to see how fresh the timeline is.</summary>
    public void Poll()
    {
        foreach (var session in _manager.GetSessions())
        {
            Timeline(session, "poll");
        }
    }

    private void Sessions(string why)
    {
        var sessions = _manager.GetSessions();
        Log.Info("media", $"{why}: {sessions.Count} session(s): {string.Join(", ", sessions.Select(s => s.SourceAppUserModelId))}; current={_manager.GetCurrentSession()?.SourceAppUserModelId ?? "(none)"}");
        foreach (var session in sessions)
        {
            if (_hooked.Add(session.SourceAppUserModelId))
            {
                session.MediaPropertiesChanged += (s, e) => { var task = Properties(s, "MediaPropertiesChanged"); };
                session.PlaybackInfoChanged += (s, _) => Playback(s, "PlaybackInfoChanged");
                session.TimelinePropertiesChanged += (s, _) => Timeline(s, "TimelinePropertiesChanged");
            }

            _ = Properties(session, why);
            Playback(session, why);
            Timeline(session, why);
        }
    }

    private static async Task Properties(GlobalSystemMediaTransportControlsSession session, string why)
    {
        try
        {
            var p = await session.TryGetMediaPropertiesAsync();
            var thumbnail = "none";
            if (p.Thumbnail is not null)
            {
                using var stream = await p.Thumbnail.OpenReadAsync();
                var bytes = new byte[Math.Min(16, (int)stream.Size)];
                await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
                thumbnail = $"{stream.Size} bytes, type \"{stream.ContentType}\", starts {Convert.ToHexString(bytes)}";
            }

            Log.Info("media", $"[{session.SourceAppUserModelId}] {why}: title=\"{p.Title}\" artist=\"{p.Artist}\" album=\"{p.AlbumTitle}\" albumArtist=\"{p.AlbumArtist}\" subtitle=\"{p.Subtitle}\" track={p.TrackNumber} albumTracks={p.AlbumTrackCount} genres=[{string.Join("|", p.Genres)}] playbackType={p.PlaybackType?.ToString() ?? "null"} thumbnail: {thumbnail}");
        }
        catch (Exception error)
        {
            Log.Info("media", $"[{session.SourceAppUserModelId}] {why}: properties failed: {error.Message}");
        }
    }

    private static void Playback(GlobalSystemMediaTransportControlsSession session, string why)
    {
        var info = session.GetPlaybackInfo();
        var c = info.Controls;
        Log.Info("media", $"[{session.SourceAppUserModelId}] {why}: status={info.PlaybackStatus} type={info.PlaybackType?.ToString() ?? "null"} rate={info.PlaybackRate?.ToString() ?? "null"} shuffle={info.IsShuffleActive?.ToString() ?? "null"} repeat={info.AutoRepeatMode?.ToString() ?? "null"} controls: play={c.IsPlayEnabled} pause={c.IsPauseEnabled} next={c.IsNextEnabled} prev={c.IsPreviousEnabled} seek={c.IsPlaybackPositionEnabled}");
    }

    private static void Timeline(GlobalSystemMediaTransportControlsSession session, string why)
    {
        var t = session.GetTimelineProperties();
        var age = DateTimeOffset.Now - t.LastUpdatedTime;
        Log.Info("media", $"[{session.SourceAppUserModelId}] {why}: position={t.Position.TotalSeconds:F2}s start={t.StartTime.TotalSeconds:F2} end={t.EndTime.TotalSeconds:F2} minSeek={t.MinSeekTime.TotalSeconds:F2} maxSeek={t.MaxSeekTime.TotalSeconds:F2} updated {age.TotalSeconds:F1}s ago");
    }
}
