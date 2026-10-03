using System.Text.Json;

namespace IdleViz.Core.Tests;

public class OverlayPayloadTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 2, 17, 30, 0, TimeSpan.Zero);
    private static readonly byte[] s_png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static NowPlaying Item(MediaStatus status = MediaStatus.Playing, string artist = "Beach House", string title = "New Year") =>
        NowPlaying.From(NowPlayingTests.Song(status, title: title, artist: artist), s_now)!;

    private static JsonElement Json(string script)
    {
        Assert.StartsWith("window.nowPlaying?.(", script, StringComparison.Ordinal);
        Assert.EndsWith(")", script, StringComparison.Ordinal);
        return JsonDocument.Parse(script["window.nowPlaying?.(".Length..^1]).RootElement;
    }

    [Fact]
    public void HasTheShapeThePageReads()
    {
        var item = Item();
        var json = Json(OverlayPayload.Script(OverlayPayload.From(item, s_png, s_now, s_now)));
        Assert.Equal(item.Id, json.GetProperty("id").GetString());
        Assert.Equal("playing", json.GetProperty("state").GetString());
        Assert.Equal("song", json.GetProperty("content").GetString());
        Assert.Equal("New Year", json.GetProperty("title").GetString());
        Assert.Equal("Beach House", json.GetProperty("artist").GetString());
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", json.GetProperty("artwork").GetString());
        Assert.False(json.GetProperty("artworkPending").GetBoolean());
        Assert.Equal(325_760, json.GetProperty("durationMs").GetInt32());
        Assert.Equal(item.Position, json.GetProperty("position").GetDouble());
        // Exactly the fields of overlay-state.js, nothing more.
        Assert.Equal(9, json.EnumerateObject().Count());
    }

    [Fact]
    public void PausedAndPodcastUseThePagesWords()
    {
        var paused = OverlayPayload.From(Item(MediaStatus.Paused), null, null, s_now);
        Assert.Equal("paused", paused.State);
        var podcast = OverlayPayload.From(Item(artist: ""), null, null, s_now);
        Assert.Equal("podcast", podcast.Content);
    }

    [Fact]
    public void NoTrackIsNull() => Assert.Equal("window.nowPlaying?.(null)", OverlayPayload.Script(null));

    [Fact]
    public void ANewTrackWithoutItsCoverYetIsPending()
    {
        var item = Item();
        Assert.True(OverlayPayload.From(item, null, s_now, s_now).ArtworkPending);
        Assert.True(OverlayPayload.From(item, null, s_now, s_now.AddSeconds(1.9)).ArtworkPending);
    }

    [Fact]
    public void ATrackWithoutACoverAfterTheWaitGetsTheNote()
    {
        var item = Item();
        Assert.False(OverlayPayload.From(item, null, s_now, s_now.AddSeconds(OverlayPayload.ArtworkWaitSeconds)).ArtworkPending);
        Assert.False(OverlayPayload.From(item, null, null, s_now).ArtworkPending);
        // A clock set back doesn't keep it waiting.
        Assert.False(OverlayPayload.From(item, null, s_now, s_now.AddSeconds(-1)).ArtworkPending);
    }

    [Fact]
    public void ACoverIsNeverPending() => Assert.False(OverlayPayload.From(Item(), s_png, s_now, s_now).ArtworkPending);

    [Fact]
    public void BytesThatAreNotAnImageAreNoCover()
    {
        var payload = OverlayPayload.From(Item(), "GIF89a"u8.ToArray(), null, s_now);
        Assert.Null(payload.Artwork);
        Assert.False(payload.ArtworkPending);
    }

    [Theory]
    [InlineData("Say \"hi\"\\")]
    [InlineData("</script><script>alert(1)</script>")]
    [InlineData("Line\u2028Paragraph\u2029End")]
    [InlineData("'); window.close(); ('")]
    [InlineData("Sigur Rós — Hoppípolla 🎵")]
    public void TitlesCannotBreakOutOfTheScript(string title)
    {
        var script = OverlayPayload.Script(OverlayPayload.From(Item(title: title), null, null, s_now));
        Assert.DoesNotContain((char)0x2028, script);
        Assert.DoesNotContain((char)0x2029, script);
        Assert.DoesNotContain('<', script);
        // Everything outside ASCII is escaped, and the title comes back unchanged.
        Assert.All(script, c => Assert.True(c < 128));
        Assert.Equal(title, Json(script).GetProperty("title").GetString());
    }
}
