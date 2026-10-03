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
        [property: JsonPropertyName("version")] string Version)
    {
        /// <summary>A Butterchurn preset or a plugin in the presets folder, served from where it is.</summary>
        /// <param name="file">The file.</param>
        /// <param name="pluginName">The name a plugin declares, if it does.</param>
        public static Entry ForFile(CustomPresetFile file, string? pluginName = null) => new(
            file.Id,
            file.Kind == CustomPresetKind.Plugin ? pluginName ?? file.Name : file.Name,
            "custom",
            file.Kind == CustomPresetKind.Plugin ? "plugin" : "preset",
            AppAddresses.PresetUrl(file.RelativePath),
            file.Version);

        /// <summary>A <c>.milk</c> file, served from its cached conversion.</summary>
        /// <param name="file">The file.</param>
        /// <param name="key">Its cache key.</param>
        public static Entry ForMilk(CustomPresetFile file, string key) =>
            new(file.Id, file.Name, "custom", "preset", AppAddresses.PresetUrl(MilkConversion.CachePath(key)), file.Version);

        /// <summary>A plugin shipped in the app's <c>web\visuals</c> folder. It runs in the same sandboxed frame as custom ones.</summary>
        /// <param name="fileName">For example <c>aurora.js</c>.</param>
        /// <param name="name">The name it declares, or its file name without the extension.</param>
        public static Entry Bundled(string fileName, string name) =>
            new($"bundled:visuals/{fileName}", name, "bundled", "plugin", $"{AppAddresses.AppOrigin}/visuals/{Uri.EscapeDataString(fileName)}", "bundled");
    }
}
