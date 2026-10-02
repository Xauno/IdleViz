namespace IdleViz.Core;

/// <summary>
/// What the command line asks for. Windows starts the app with the <c>idleviz://</c> URL as an
/// argument, and a second copy hands its arguments to the one already running.
/// </summary>
public sealed record LaunchOptions
{
    /// <summary>The command from an <c>idleviz://</c> URL, if one was passed.</summary>
    public UrlCommand? Command { get; init; }

    /// <summary>Debug builds only: keep the visualizer open on input, so it can be inspected. Trigger it again to close.</summary>
    public bool NoDismiss { get; init; }

    /// <summary>Debug builds only: open the settings window at launch.</summary>
    public bool ShowSettings { get; init; }

    /// <summary>Debug builds only: open the tray flyout, as a left-click on the icon does.</summary>
    public bool ShowFlyout { get; init; }

    /// <summary>Debug builds only: open the tray menu at the pointer, as a right-click on the icon does.</summary>
    public bool ShowMenu { get; init; }

    /// <summary>Debug builds only: open the visualizer three seconds after launch.</summary>
    public bool OpenAtLaunch { get; init; }

    public static LaunchOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var options = new LaunchOptions();
        foreach (var argument in arguments)
        {
            options = argument.ToLowerInvariant() switch
            {
                "--no-dismiss" => options with { NoDismiss = true },
                "--show-settings" => options with { ShowSettings = true },
                "--show-flyout" => options with { ShowFlyout = true },
                "--show-menu" => options with { ShowMenu = true },
                "--open-at-launch" => options with { OpenAtLaunch = true },
                _ => UrlCommands.Parse(argument) is { } command ? options with { Command = command } : options,
            };
        }

        return options;
    }

    /// <summary>
    /// Splits a command line as Windows hands it to a running copy: one string, arguments
    /// separated by spaces, with double quotes around any that contain a space.
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string? commandLine)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return arguments;
        }

        var current = new System.Text.StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var character in commandLine)
        {
            if (character == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (started)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
            }
            else
            {
                current.Append(character);
                started = true;
            }
        }

        if (started)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }
}
