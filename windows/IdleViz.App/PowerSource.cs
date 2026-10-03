using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace IdleViz.App;

/// <summary>
/// Whether the PC runs on battery. Windows reports changes through the hidden window, so nothing
/// polls. Ported from <c>PowerSource.swift</c>.
/// </summary>
internal sealed class PowerSource
{
    private readonly bool _pretend;

    /// <param name="window">Hears the power notices from Windows.</param>
    /// <param name="pretendBattery">Debug builds: behave as a laptop running on its battery.</param>
    public PowerSource(HotkeyWindow window, bool pretendBattery = false)
    {
        _pretend = pretendBattery;
        HasBattery = pretendBattery || Read().HasBattery;
        OnBattery = pretendBattery || Read().OnBattery;
        window.PowerStatusChanged += Refresh;
    }

    /// <summary>False on a desktop PC, where the battery settings are hidden. A UPS doesn't count.</summary>
    public bool HasBattery { get; }

    public bool OnBattery { get; private set; }

    /// <summary>Raised when the PC is plugged in or unplugged.</summary>
    public event Action? Changed;

    // The notice also comes when only the charge level changed.
    private void Refresh()
    {
        var now = _pretend || Read().OnBattery;
        if (now == OnBattery)
        {
            return;
        }

        OnBattery = now;
        Changed?.Invoke();
    }

    private static (bool HasBattery, bool OnBattery) Read()
    {
        if (!PInvoke.GetSystemPowerStatus(out var status))
        {
            return (false, false);
        }

        var role = (PlatformRole)(int)PInvoke.PowerDeterminePlatformRoleEx(POWER_PLATFORM_ROLE_VERSION.POWER_PLATFORM_ROLE_V2);
        return (PowerStatus.HasBattery(status.BatteryFlag, role), PowerStatus.OnBattery(status.ACLineStatus, status.BatteryFlag, role));
    }
}
