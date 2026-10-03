using System.Text.Json;

namespace IdleViz.Core;

/// <summary>One preset or plugin that couldn't be loaded, for the "Failed to load" list in settings.</summary>
public readonly record struct PresetFailure(string Id, string Error);

/// <summary>
/// The page's answer to <c>window.idlevizStatus()</c>. The page runs preset and plugin code, so the
/// reply is untrusted: only the expected shape is read, and strings are cut short. Ported from
/// <c>PageStatus.swift</c>.
/// </summary>
/// <param name="Preset">The id of the preset on screen, or null if none is.</param>
/// <param name="Frames">Frames rendered since the page loaded.</param>
/// <param name="AudioFrames">Audio frames the page has received since it loaded.</param>
/// <param name="Failed">Presets and plugins that failed to load, each with its error message.</param>
public sealed record PageStatus(string? Preset, long Frames, long AudioFrames, IReadOnlyList<PresetFailure> Failed)
{
    public const int MaxTextLength = 300;
    public const int MaxFailures = 200;

    // Deeper than any honest reply, so a hostile one can't make the parser work hard.
    private static readonly JsonDocumentOptions s_options = new() { MaxDepth = 8 };

    /// <summary>Reads what <c>ExecuteScriptAsync</c> returned (JSON). Null if it isn't a status object at all.</summary>
    public static PageStatus? FromReply(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, s_options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var preset = root.TryGetProperty("preset", out var value) && value.ValueKind == JsonValueKind.String ? Clip(value.GetString()!) : null;
            var failed = new List<PresetFailure>();
            if (root.TryGetProperty("failed", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray().Take(MaxFailures))
                {
                    if (item.ValueKind != JsonValueKind.Object
                        || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 } idText)
                    {
                        continue;
                    }

                    var error = item.TryGetProperty("error", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString()! : "Failed to load";
                    failed.Add(new PresetFailure(Clip(idText), Clip(error)));
                }
            }

            return new PageStatus(preset, Count(root, "frames"), Count(root, "audioFrames"), failed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Cuts text from the page down to a length that is safe to log and show.</summary>
    public static string Clip(string text) => text.Length > MaxTextLength ? text[..MaxTextLength] : text;

    /// <summary>Whether two replies list the same failures.</summary>
    public bool SameFailures(PageStatus? other) => other is not null && Failed.SequenceEqual(other.Failed);

    private static long Count(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
        && double.IsFinite(number) && number >= 0 && number < 1e15
            ? (long)number
            : 0;
}

/// <summary>
/// Decides when the page has stopped answering. A preset or plugin stuck in a loop freezes the
/// page's renderer; the only cure is a new one. Ported from <c>PageWatchdog</c> in <c>PageStatus.swift</c>.
/// </summary>
public sealed class PageWatchdog
{
    public const double ReloadAfterSeconds = 3;

    private double? _lastReply;

    /// <summary>Call when the status checks start (the window opened, or the page was replaced).</summary>
    public void Start(double now) => _lastReply = now;

    public void Stop() => _lastReply = null;

    public void Replied(double now)
    {
        if (_lastReply is not null)
        {
            _lastReply = now;
        }
    }

    /// <summary>Call before each status check. True means replace the page; the count then starts over.</summary>
    public bool ShouldReload(double now)
    {
        if (_lastReply is not { } last || now - last < ReloadAfterSeconds)
        {
            return false;
        }

        _lastReply = now;
        return true;
    }
}
