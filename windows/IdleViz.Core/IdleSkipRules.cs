namespace IdleViz.Core;

/// <summary>What <c>SHQueryUserNotificationState</c> reports: whether the shell thinks someone is busy.</summary>
public enum ShellState
{
    /// <summary>The call failed, or a value newer than this list.</summary>
    Unknown = 0,

    /// <summary>A screen saver, a locked session, or another user's session in front.</summary>
    NotPresent = 1,

    /// <summary>An app is fullscreen.</summary>
    Busy = 2,

    /// <summary>A Direct3D game or player is in exclusive fullscreen.</summary>
    Direct3DFullScreen = 3,

    /// <summary>Windows presentation mode is on.</summary>
    PresentationMode = 4,

    AcceptsNotifications = 5,

    /// <summary>The first hour after Windows was set up.</summary>
    QuietTime = 6,

    /// <summary>A Store app is in front. Spotify from the Microsoft Store is one, so this doesn't skip.</summary>
    App = 7,
}

/// <summary>One app playing through the speakers: the loudest sample it played lately, 0 to 1.</summary>
public readonly record struct AppSound(int ProcessId, string ProcessName, float Peak);

/// <summary>What the idle trigger sees of the session when it fires.</summary>
public sealed record IdleConditions(bool SessionLocked, bool OnConsole, ShellState Shell, IReadOnlyList<AppSound> Sounds);

public enum IdleSkipKind
{
    SessionLocked,

    /// <summary>This session isn't the one at the screen: remote desktop, or another user switched in.</summary>
    NotOnConsole,

    Fullscreen,
    Presentation,

    /// <summary>Another app is making sound: most likely a video or a call, maybe in a normal window.</summary>
    OtherAppPlaying,
}

/// <summary>Why the idle trigger didn't open. <c>Holder</c> names the app that is playing, for the log.</summary>
public readonly record struct IdleSkip(IdleSkipKind Kind, string? Holder = null);

/// <summary>
/// The idle-only skip rules. Manual triggers skip these checks. The Mac reads the display-sleep
/// assertions, which Windows only gives an administrator; instead the shell's fullscreen and
/// presentation state is read, and, as the owner decided (R3), any app other than Spotify that is
/// making sound also blocks.
/// </summary>
public static class IdleSkipRules
{
    /// <summary>
    /// Quieter than this is silence: about −60 dBFS. A session can stay open while playing nothing,
    /// or play digital near-silence; a quiet scene in a video is still well above it.
    /// </summary>
    public const float SoundThreshold = 0.001f;

    /// <param name="conditions">The session right now.</param>
    /// <param name="ignoredProcessIds">Spotify's and IdleViz's own processes, whose sound doesn't count.</param>
    public static IdleSkip? Skip(IdleConditions conditions, IReadOnlySet<int> ignoredProcessIds)
    {
        if (conditions.SessionLocked || conditions.Shell == ShellState.NotPresent)
        {
            return new IdleSkip(IdleSkipKind.SessionLocked);
        }

        if (!conditions.OnConsole)
        {
            return new IdleSkip(IdleSkipKind.NotOnConsole);
        }

        switch (conditions.Shell)
        {
            case ShellState.Busy or ShellState.Direct3DFullScreen:
                return new IdleSkip(IdleSkipKind.Fullscreen);
            case ShellState.PresentationMode:
                return new IdleSkip(IdleSkipKind.Presentation);
        }

        foreach (var sound in conditions.Sounds)
        {
            if (sound.Peak > SoundThreshold && !ignoredProcessIds.Contains(sound.ProcessId))
            {
                return new IdleSkip(IdleSkipKind.OtherAppPlaying, sound.ProcessName);
            }
        }

        return null;
    }

    /// <summary>The reason as a log line.</summary>
    public static string Describe(IdleSkip skip) => skip.Kind switch
    {
        IdleSkipKind.SessionLocked => "the session is locked",
        IdleSkipKind.NotOnConsole => "this session isn't the one at the screen",
        IdleSkipKind.Fullscreen => "an app is fullscreen",
        IdleSkipKind.Presentation => "presentation mode is on",
        _ => $"{skip.Holder} is playing sound",
    };
}
