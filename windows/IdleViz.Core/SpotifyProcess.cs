namespace IdleViz.Core;

/// <summary>One running process, as the process snapshot lists it.</summary>
public readonly record struct ProcessEntry(int Id, int ParentId, string ExeName);

/// <summary>Which process the Spotify capture listens to.</summary>
public static class SpotifyProcess
{
    public const string ExeName = "Spotify.exe";

    /// <summary>
    /// The first process of Spotify's tree: a <c>Spotify.exe</c> whose parent isn't one. Spotify runs
    /// as about seven processes and plays from a child, so the capture takes the whole tree under it
    /// (W2 spike). Choosing by parent, not by main window, also finds a Spotify hidden in the tray.
    /// Null when Spotify isn't running.
    /// </summary>
    public static int? Root(IEnumerable<ProcessEntry> processes)
    {
        var spotify = processes.Where(p => string.Equals(p.ExeName, ExeName, StringComparison.OrdinalIgnoreCase)).ToList();
        var ids = spotify.Select(p => p.Id).ToHashSet();
        // A parent id can be reused by an unrelated process after the parent ends, so a parent that is itself counts too.
        var roots = spotify.Where(p => !ids.Contains(p.ParentId) || p.ParentId == p.Id).Select(p => p.Id).ToList();
        // Should there be two, the one with the most Spotify children is the app; the other is likely a helper left behind.
        return roots.Count == 0
            ? null
            : roots.OrderByDescending(id => spotify.Count(p => p.ParentId == id && p.Id != id)).ThenBy(id => id).First();
    }
}
