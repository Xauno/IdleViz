using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace IdleViz.App;

/// <summary>Reads the connected displays, for telling a real display change from a notice about nothing.</summary>
internal static class Displays
{
    private const uint PrimaryFlag = 1; // MONITORINFOF_PRIMARY

    /// <summary>Every display as it is now, the primary one first and the rest by name.</summary>
    public static unsafe IReadOnlyList<Display> Current()
    {
        var found = new List<(bool Primary, Display Display)>();
        MONITORENUMPROC visit = (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { monitorInfo = { cbSize = (uint)sizeof(MONITORINFOEXW) } };
            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                var bounds = info.monitorInfo.rcMonitor;
                var dpi = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded ? (int)dpiX : 96;
                found.Add((
                    (info.monitorInfo.dwFlags & PrimaryFlag) != 0,
                    new Display(info.szDevice.ToString(), bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top, dpi)));
            }

            return true;
        };
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, visit, 0);
        GC.KeepAlive(visit);
        return [.. found.OrderByDescending(item => item.Primary).ThenBy(item => item.Display.Name, StringComparer.Ordinal).Select(item => item.Display)];
    }

    public static string Describe(IReadOnlyList<Display> layout) =>
        string.Join(", ", layout.Select(display => $"{display.Width}×{display.Height} at {display.Left},{display.Top} ({display.Dpi * 100 / 96} %)"));
}
