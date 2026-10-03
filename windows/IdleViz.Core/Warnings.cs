namespace IdleViz.Core;

/// <summary>The problems that turn the tray icon yellow. Their order here is the order of the flyout's rows.</summary>
public enum WarningKind
{
    /// <summary>The capture of Spotify's sound can't be started.</summary>
    AudioCapture,

    /// <summary>The Windows media controls can't be reached.</summary>
    NowPlaying,
}

/// <summary>One problem that is showing.</summary>
/// <param name="Kind">Which problem.</param>
/// <param name="Details">The error text Windows gave.</param>
public sealed record Warning(WarningKind Kind, string Details)
{
    /// <summary>The first line of the flyout row, and the title of the details dialog.</summary>
    public string Title => Kind switch
    {
        WarningKind.AudioCapture => "Spotify audio can't be captured",
        _ => "Can't read what Spotify is playing",
    };

    /// <summary>What the problem means for the user, for the details dialog.</summary>
    public string Explanation => Kind switch
    {
        WarningKind.AudioCapture =>
            "IdleViz couldn't start capturing Spotify's sound, so the visuals don't move to the music.",
        _ =>
            "IdleViz couldn't reach the Windows media controls, so it can't tell whether Spotify has a track, and the overlay may be missing or out of date.",
    };
}

/// <summary>
/// The problems showing right now. Each source reports its own state whenever it checks, with the
/// error text or null for "fine"; a change in the list is announced once. Not thread-safe: report
/// from one thread.
/// </summary>
public sealed class WarningList
{
    public const string Tooltip = "IdleViz";
    public const string WarningTooltip = "IdleViz, something needs attention";

    private readonly SortedDictionary<WarningKind, Warning> _current = [];

    /// <summary>Raised when a problem appeared, went away or changed its text.</summary>
    public event Action? Changed;

    /// <summary>One entry per problem, in the order of <see cref="WarningKind"/>.</summary>
    public IReadOnlyList<Warning> Current => [.. _current.Values];

    public bool Any => _current.Count > 0;

    /// <summary>The tray icon's tooltip.</summary>
    public string CurrentTooltip => Any ? WarningTooltip : Tooltip;

    /// <summary>Records the result of a check: the error text, or null when that part works.</summary>
    public void Report(WarningKind kind, string? problem)
    {
        if (problem is null)
        {
            if (_current.Remove(kind))
            {
                Changed?.Invoke();
            }

            return;
        }

        var details = string.IsNullOrWhiteSpace(problem) ? "No details were given." : problem.Trim();
        if (_current.TryGetValue(kind, out var known) && known.Details == details)
        {
            return;
        }

        _current[kind] = new Warning(kind, details);
        Changed?.Invoke();
    }
}
