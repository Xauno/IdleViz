namespace IdleViz.Core;

/// <summary>The "Keep screen awake" setting, stored in minutes.</summary>
public static class KeepAwakeSetting
{
    public const string Key = "keepAwakeLimit";
    public const int DefaultMinutes = 60;

    public static IReadOnlyList<int> Choices { get; } = [30, 60, 120, 240];

    public static string Label(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} min";
        }

        return minutes == 60 ? "1 hour" : $"{minutes / 60} hours";
    }
}

/// <summary>The "Different times on battery" switch and the two values it reveals, stored in minutes.</summary>
public static class BatteryTimesSetting
{
    public const string EnabledKey = "useBatteryTimes";
    public const string IdleTimeoutKey = "idleTimeoutBattery";
    public const string KeepAwakeKey = "keepAwakeLimitBattery";
}

/// <summary>What kind of computer Windows says this is (<c>POWER_PLATFORM_ROLE</c>).</summary>
public enum PlatformRole
{
    Unspecified = 0,
    Desktop = 1,
    Mobile = 2,
    Workstation = 3,
    EnterpriseServer = 4,
    SohoServer = 5,
    AppliancePc = 6,
    PerformanceServer = 7,
    Slate = 8,
}

/// <summary>What <c>GetSystemPowerStatus</c> reports, reduced to the two questions the app asks.</summary>
public static class PowerStatus
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownBattery = 255;
    private const byte AcOffline = 0;

    /// <summary>
    /// Whether the PC has a battery of its own. A UPS on a desktop PC reports as a battery too, but
    /// it doesn't count: that is a desktop on mains that is about to lose them, not a laptop
    /// unplugged. So a battery only counts on a PC that Windows calls a laptop or a tablet.
    /// </summary>
    public static bool HasBattery(byte batteryFlag, PlatformRole role) =>
        batteryFlag != UnknownBattery && (batteryFlag & NoSystemBattery) == 0 && role is PlatformRole.Mobile or PlatformRole.Slate;

    /// <summary>Whether the PC runs on its battery right now.</summary>
    public static bool OnBattery(byte acLineStatus, byte batteryFlag, PlatformRole role) =>
        HasBattery(batteryFlag, role) && acLineStatus == AcOffline;
}

/// <summary>The idle timeout and the keep-awake limit, with the values that replace them on battery. Ported from <c>Timing.swift</c>.</summary>
/// <param name="IdleMinutes">0 means the idle trigger is off.</param>
/// <param name="KeepAwakeMinutes">How long the open visualizer keeps the display awake.</param>
/// <param name="UseBatteryTimes">Whether the two battery values apply while on battery.</param>
/// <param name="BatteryIdleMinutes">The idle timeout on battery.</param>
/// <param name="BatteryKeepAwakeMinutes">The keep-awake limit on battery.</param>
public sealed record TimingSettings(int IdleMinutes, int KeepAwakeMinutes, bool UseBatteryTimes, int BatteryIdleMinutes, int BatteryKeepAwakeMinutes)
{
    /// <summary>Reads the stored settings. A battery value that was never set is a copy of the plugged-in one.</summary>
    public static TimingSettings Read(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // A keep-awake limit of zero or less would close the window the moment it opens.
        int? Limit(string key) => settings.GetInt(key) is { } minutes && minutes > 0 ? minutes : null;

        var idle = IdleTimeoutSetting.Minutes(settings);
        var keepAwake = Limit(KeepAwakeSetting.Key) ?? KeepAwakeSetting.DefaultMinutes;
        return new TimingSettings(
            idle,
            keepAwake,
            settings.GetBool(BatteryTimesSetting.EnabledKey) ?? false,
            settings.GetInt(BatteryTimesSetting.IdleTimeoutKey) ?? idle,
            Limit(BatteryTimesSetting.KeepAwakeKey) ?? keepAwake);
    }

    /// <summary>Seconds of no input before opening, or null when the idle trigger is off.</summary>
    public double? IdleTimeout(bool onBattery) =>
        IdleTimeoutSetting.Timeout(UseBatteryTimes && onBattery ? BatteryIdleMinutes : IdleMinutes);

    /// <summary>How long the open visualizer keeps the display awake, in seconds.</summary>
    public double KeepAwakeLimit(bool onBattery) =>
        (UseBatteryTimes && onBattery ? BatteryKeepAwakeMinutes : KeepAwakeMinutes) * 60.0;

    /// <summary>Seconds until the limit is reached, counted from when the window opened. Zero or less means close now.</summary>
    public static double Remaining(double limit, double openedAt, double now) => limit - (now - openedAt);
}
