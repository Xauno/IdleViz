namespace IdleViz.Core;

/// <summary>What <see cref="IdleScheduler.Check"/> says to do next.</summary>
/// <param name="Fire">Try to open now.</param>
/// <param name="WaitSeconds">Otherwise check again after this many seconds, or null when the idle trigger is off.</param>
public readonly record struct IdleStep(bool Fire, double? WaitSeconds)
{
    public static IdleStep Open { get; } = new(true, null);

    public static IdleStep Off { get; } = new(false, null);

    public static IdleStep Wait(double seconds) => new(false, seconds);
}

/// <summary>
/// Decides when the idle trigger fires, from the time since the last input. Instead of polling on a
/// fixed interval, each check says when the next one is due: at the earliest moment the timeout
/// could be reached. Each idle period gets one attempt. If that attempt is blocked (no track, a
/// locked session, a video playing), it waits for new input. Ported from <c>IdleScheduler.swift</c>.
/// </summary>
public sealed class IdleScheduler(double? timeoutSeconds)
{
    // Two readings of the same idle period differ by timer jitter; new input moves it by more.
    private const double Tolerance = 0.5;

    // When the last input happened (uptime), for the idle period that was already attempted.
    private double? _attemptedPeriod;

    /// <summary>Seconds of no input before opening, or null when the idle trigger is off.</summary>
    public double? TimeoutSeconds { get; set; } = timeoutSeconds;

    /// <param name="now">Seconds since the PC started.</param>
    /// <param name="idle">Seconds since the last input.</param>
    public IdleStep Check(double now, double idle)
    {
        if (TimeoutSeconds is not { } timeout || timeout <= 0)
        {
            return IdleStep.Off;
        }

        var lastInput = now - idle;
        if (_attemptedPeriod is { } attempted && Math.Abs(lastInput - attempted) < Tolerance)
        {
            // Still the same idle period. Any new input restarts the full timeout, so this is the earliest it could fire.
            return IdleStep.Wait(timeout);
        }

        if (idle < timeout)
        {
            return IdleStep.Wait(timeout - idle);
        }

        _attemptedPeriod = lastInput;
        return IdleStep.Open;
    }

    /// <summary>
    /// Call when the keep-awake limit closes the visualizer. The PC is still idle, so without this
    /// the trigger would open it again at once. The current idle period counts as used.
    /// </summary>
    public void WaitForInput(double now, double idle) => _attemptedPeriod = now - idle;
}

/// <summary>The "Start after idle" setting, stored in minutes. 0 means off.</summary>
public static class IdleTimeoutSetting
{
    public const string Key = "idleTimeout";
    public const int DefaultMinutes = 5;

    public static IReadOnlyList<int> Choices { get; } = [5, 10, 15, 30];

    /// <summary>The stored minutes, or the default when never set.</summary>
    public static int Minutes(SettingsStore settings) => settings.GetInt(Key) ?? DefaultMinutes;

    public static double? Timeout(int minutes) => minutes > 0 ? minutes * 60.0 : null;
}
