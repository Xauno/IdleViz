namespace IdleViz.Core;

/// <summary>Whether Windows starts the app when the user signs in.</summary>
public enum StartupState
{
    Off,
    On,

    /// <summary>The app is registered, but the user switched it off in Windows' own list of startup apps.</summary>
    DisabledInWindows,
}

/// <summary>
/// "Run at startup", read from what Windows has stored and never kept in the settings file, since
/// the user can also switch it off in Windows. The app registers itself under the user's Run key;
/// Windows keeps its own on/off switch for each entry next to it (StartupApproved).
/// </summary>
public static class StartupEntry
{
    /// <summary>The name of the value under the Run key, and of its switch under StartupApproved.</summary>
    public const string Name = "IdleViz";

    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>The page of Windows Settings that lists the startup apps.</summary>
    public static Uri WindowsSettings { get; } = new("ms-settings:startupapps");

    /// <summary>What to store under the Run key: the path in quotes, as it may contain spaces.</summary>
    public static string Command(string executablePath) => $"\"{executablePath}\"";

    /// <param name="runValue">The app's value under the Run key, or null if there is none.</param>
    /// <param name="approved">The app's value under StartupApproved, or null if there is none.</param>
    /// <param name="executablePath">The running copy's own path.</param>
    public static StartupState State(string? runValue, IReadOnlyList<byte>? approved, string executablePath)
    {
        // An entry for another copy (a Debug build, an old install folder) is not this copy's.
        if (runValue is null || !string.Equals(runValue.Trim(), Command(executablePath), StringComparison.OrdinalIgnoreCase))
        {
            return StartupState.Off;
        }

        // Windows writes 2 (or 6) in the first byte for on and 3 (or 7) for off: the low bit means disabled.
        return approved is { Count: > 0 } && (approved[0] & 1) != 0 ? StartupState.DisabledInWindows : StartupState.On;
    }
}
