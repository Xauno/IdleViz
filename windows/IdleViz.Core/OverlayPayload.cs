using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleViz.Core;

/// <summary>
/// What the page's <c>window.nowPlaying(json)</c> receives (<c>overlay-state.js</c>). The page never
/// uses the network, so the cover travels inside as a <c>data:</c> URL. Ported from
/// <c>OverlayPayload.swift</c>.
/// </summary>
public sealed record OverlayPayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("artwork")] string? Artwork,
    [property: JsonPropertyName("artworkPending")] bool ArtworkPending,
    [property: JsonPropertyName("durationMs")] int DurationMs,
    [property: JsonPropertyName("position")] double Position)
{
    /// <summary>
    /// How long after a track appears its cover still counts as on its way. Until then the page
    /// leaves the square empty instead of showing the music note, so the note doesn't flash up
    /// before every cover. Spotify hands covers over 20 to 150 ms after the track (W3 and W4 logs);
    /// a track that has none after this long gets the note.
    /// </summary>
    public const double ArtworkWaitSeconds = 2;

    private static readonly JsonSerializerOptions s_json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    private static readonly string s_lineSeparator = ((char)0x2028).ToString();
    private static readonly string s_paragraphSeparator = ((char)0x2029).ToString();

    /// <param name="item">The current track.</param>
    /// <param name="artwork">Its cover as Spotify gave it, or null.</param>
    /// <param name="trackSince">When this track became the current one (<see cref="SpotifyTracker.CurrentSince"/>).</param>
    /// <param name="now">Now, to tell whether the cover may still come.</param>
    public static OverlayPayload From(NowPlaying item, byte[]? artwork, DateTimeOffset? trackSince, DateTimeOffset now)
    {
        var dataUrl = artwork is null ? null : global::IdleViz.Core.Artwork.DataUrl(artwork);
        return new OverlayPayload(
            Id: item.Id,
            State: item.State == SpotifyPlayerState.Paused ? "paused" : "playing",
            Content: item.Content == SpotifyContent.Podcast ? "podcast" : "song",
            Title: item.Title,
            Artist: item.Artist,
            Artwork: dataUrl,
            ArtworkPending: dataUrl is null && IsArtworkPending(trackSince, now),
            DurationMs: item.DurationMs,
            Position: item.Position);
    }

    /// <summary>Whether a track without a cover may still get one: it became current less than <see cref="ArtworkWaitSeconds"/> ago.</summary>
    public static bool IsArtworkPending(DateTimeOffset? trackSince, DateTimeOffset now) =>
        trackSince is { } since && (now - since).TotalSeconds is >= 0 and < ArtworkWaitSeconds;

    /// <summary>
    /// The script that hands a payload to the page, or <c>null</c> when there's no track. Text from
    /// Spotify goes through the JSON encoder, never into the script by concatenation; it escapes
    /// quotes, backslashes and everything outside ASCII, U+2028 and U+2029 included.
    /// </summary>
    public static string Script(OverlayPayload? payload)
    {
        var json = payload is null ? "null" : JsonSerializer.Serialize(payload, s_json);
        // Already escaped by the default encoder; kept so a change of encoder can't let them through.
        json = json.Replace(s_lineSeparator, "\\u2028", StringComparison.Ordinal).Replace(s_paragraphSeparator, "\\u2029", StringComparison.Ordinal);
        return $"window.nowPlaying?.({json})";
    }
}
