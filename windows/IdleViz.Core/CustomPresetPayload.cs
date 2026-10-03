using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleViz.Core;

/// <summary>
/// What <c>window.setCustomPresets</c> gets: the plugins bundled with the app, the custom presets
/// folder (both W7c), and the presets that hung the page. Ported from <c>CustomPresets.swift</c>.
/// </summary>
public sealed record CustomPresetPayload(
    [property: JsonPropertyName("entries")] IReadOnlyList<CustomPresetPayload.Entry> Entries,
    [property: JsonPropertyName("hung")] IReadOnlyList<string> Hung)
{
    public static CustomPresetPayload Empty { get; } = new([], []);

    /// <summary>The call that sends it. Characters that would end a JavaScript string are escaped by the serializer.</summary>
    [JsonIgnore]
    public string Script => $"window.setCustomPresets?.({JsonSerializer.Serialize(this)})";

    /// <param name="Id">For example <c>custom:Tunnel.json</c>.</param>
    /// <param name="Name">What settings and the page show.</param>
    /// <param name="Source">"bundled" or "custom".</param>
    /// <param name="Kind">"preset" (Butterchurn JSON) or "plugin" (a JavaScript module).</param>
    /// <param name="Url">Where the page loads it from.</param>
    /// <param name="Version">Changes when the file does, so an edited file gets a fresh start.</param>
    public sealed record Entry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("version")] string Version);
}
