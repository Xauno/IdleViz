using System.Globalization;
using System.Text.Json;

namespace IdleViz.Core;

/// <summary>
/// The check for a newer release: once a day the app asks GitHub for the latest one and, if it is
/// newer than the running copy, the tray flyout offers it. Nothing is downloaded or installed.
/// </summary>
public static class UpdateCheck
{
    /// <summary>The "Check for updates" switch.</summary>
    public const string EnabledKey = "checkForUpdates";

    /// <summary>When GitHub last answered, in seconds since 1970.</summary>
    public const string LastCheckKey = "updateLastCheck";

    /// <summary>The latest release GitHub named then, kept so the row is there again right after a restart.</summary>
    public const string LatestKey = "updateLatestVersion";

    public static TimeSpan Interval { get; } = TimeSpan.FromHours(24);

    /// <summary>How long to wait after a check that failed, for example with no network.</summary>
    public static TimeSpan RetryInterval { get; } = TimeSpan.FromHours(1);

    public static Uri LatestRelease { get; } = new("https://api.github.com/repos/Xauno/IdleViz/releases/latest");

    /// <summary>Where the row sends the user. Fixed, so nothing GitHub answers decides which page opens.</summary>
    public static Uri ReleasePage { get; } = new("https://github.com/Xauno/IdleViz/releases/latest");

    /// <summary>The version the debug switch <c>--pretend-update</c> offers.</summary>
    public static Version PretendVersion { get; } = new(99, 0, 0, 0);

    /// <summary>On unless it was switched off.</summary>
    public static bool IsEnabled(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.GetBool(EnabledKey) ?? true;
    }

    /// <summary>When GitHub last answered, or null if it never did.</summary>
    public static DateTimeOffset? LastCheck(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Anything outside what a date can hold reads as never.
        var seconds = settings.GetDouble(LastCheckKey);
        return seconds is >= 0 and < 100_000_000_000 ? DateTimeOffset.UnixEpoch.AddSeconds(seconds.Value) : null;
    }

    /// <summary>Stores what GitHub answered and when.</summary>
    public static void Store(SettingsStore settings, Version latest, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(latest);
        settings.SetDouble(LastCheckKey, (now - DateTimeOffset.UnixEpoch).TotalSeconds);
        settings.SetString(LatestKey, Label(latest));
    }

    /// <summary>
    /// Reads the app's own version or a release tag ("v0.1.2"). Null for anything but one to four whole
    /// numbers with dots between them, so a tag like "v0.2.0-beta" is never offered. Missing numbers
    /// are zero: 0.1 and 0.1.0 are the same version.
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var fields = trimmed.Split('.');
        if (fields.Length > 4)
        {
            return null;
        }

        var parts = new int[4];
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (field.Length is 0 or > 9 || !field.All(char.IsAsciiDigit))
            {
                return null;
            }

            parts[index] = int.Parse(field, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }

    /// <summary>The running copy's version from its assembly, with every number filled in.</summary>
    public static Version? Installed(Version? assemblyVersion) =>
        assemblyVersion is null
            ? null
            : new Version(
                assemblyVersion.Major, assemblyVersion.Minor, Math.Max(assemblyVersion.Build, 0), Math.Max(assemblyVersion.Revision, 0));

    /// <summary>Three numbers, as the releases are named ("0.2.0"), or four if the last one isn't zero.</summary>
    public static string Label(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var whole = Installed(version)!;
        return whole.ToString(whole.Revision > 0 ? 4 : 3);
    }

    /// <summary>
    /// The version in GitHub's answer about the latest release. Null if the answer can't be read,
    /// or names a draft, a prerelease or a tag that isn't a plain version.
    /// </summary>
    public static Version? LatestVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var release = document.RootElement;
            if (release.ValueKind != JsonValueKind.Object
                || !release.TryGetProperty("tag_name", out var tag)
                || tag.ValueKind != JsonValueKind.String
                || IsTrue(release, "draft")
                || IsTrue(release, "prerelease"))
            {
                return null;
            }

            return ParseVersion(tag.GetString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// How long until the next check is due; zero means now. A last check in the future (the clock
    /// was set back) counts as never.
    /// </summary>
    public static TimeSpan Wait(DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        if (lastCheck is not { } last || last > now)
        {
            return TimeSpan.Zero;
        }

        var left = Interval - (now - last);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>The version to offer: the latest release, if it is newer than the running copy.</summary>
    public static Version? Available(Version? installed, Version? latest) =>
        installed is not null && latest is not null && latest > installed ? latest : null;

    private static bool IsTrue(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
