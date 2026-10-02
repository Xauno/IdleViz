namespace IdleViz.Core;

/// <summary>What asked the visualizer to open.</summary>
public enum TriggerSource
{
    Hotkey,
    UrlScheme,
    Settings,
    Idle,
}

public static class TriggerSourceExtensions
{
    /// <summary>
    /// Manual triggers mean someone is at the PC: they skip the idle skip rules
    /// and flash the tray icon when the open is refused.
    /// </summary>
    public static bool IsManual(this TriggerSource source) => source != TriggerSource.Idle;
}

/// <summary>Commands accepted on the external <c>idleviz://</c> URL scheme.</summary>
public enum UrlCommand
{
    Open,
}

public static class UrlCommands
{
    public const string Scheme = "idleviz";

    /// <summary>Reads a command from a URL as Windows hands it over on the command line, or null if it isn't one.</summary>
    public static UrlCommand? Parse(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.Host.ToLowerInvariant() switch
        {
            "open" => UrlCommand.Open,
            _ => null,
        };
    }
}
