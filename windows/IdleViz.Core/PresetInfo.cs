using System.Text.Json;

namespace IdleViz.Core;

/// <summary>One preset or plugin the page knows about, for the pickers in settings. Ported from <c>PresetSettings.swift</c>.</summary>
/// <param name="Id">For example <c>bundled:Geiss - Swirlie 5</c>.</param>
/// <param name="Name">What settings shows.</param>
/// <param name="Source">"bundled" or "custom".</param>
public sealed record PresetInfo(string Id, string Name, string Source)
{
    public const int MaxCount = 5000;
    public const int MaxTextLength = 300;

    // Deeper than any honest reply, so a hostile one can't make the parser work hard.
    private static readonly JsonDocumentOptions s_options = new() { MaxDepth = 4 };

    public bool IsCustom => Source == "custom";

    /// <summary>
    /// Reads the page's answer to <c>window.idlevizPresets()</c>, as JSON. The page runs preset and
    /// plugin code, so the reply is untrusted: wrong shapes are dropped and strings are cut short.
    /// </summary>
    public static IReadOnlyList<PresetInfo> List(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json, s_options);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<PresetInfo>();
            foreach (var item in document.RootElement.EnumerateArray().Take(MaxCount))
            {
                if (item.ValueKind != JsonValueKind.Object
                    || Text(item, "id") is not { Length: > 0 and <= MaxTextLength } id
                    || Text(item, "name") is not { } name
                    || !seen.Add(id))
                {
                    continue;
                }

                var source = Text(item, "source") == "custom" ? "custom" : "bundled";
                list.Add(new PresetInfo(id, PageStatus.Clip(name), source));
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>A readable name for an id whose preset is no longer in the library ("custom:My Preset" → "My Preset").</summary>
    public static string FallbackName(string id)
    {
        var colon = id.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? id : id[(colon + 1)..];
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
