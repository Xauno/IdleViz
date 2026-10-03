using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Devices.Display;
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
        var models = Models();
        var found = new List<(bool Primary, Display Display)>();
        MONITORENUMPROC visit = (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { monitorInfo = { cbSize = (uint)sizeof(MONITORINFOEXW) } };
            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                var bounds = info.monitorInfo.rcMonitor;
                var dpi = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded ? (int)dpiX : 96;
                var name = info.szDevice.ToString();
                found.Add((
                    (info.monitorInfo.dwFlags & PrimaryFlag) != 0,
                    new Display(name, bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top, dpi, models.GetValueOrDefault(name))));
            }

            return true;
        };
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, visit, 0);
        GC.KeepAlive(visit);
        return [.. found.OrderByDescending(item => item.Primary).ThenBy(item => item.Display.Name, StringComparer.Ordinal).Select(item => item.Display)];
    }

    public static string Describe(IReadOnlyList<Display> layout) =>
        string.Join(", ", layout.Select(display => $"{DisplayLabel.For(display)} {display.Width}×{display.Height} at {display.Left},{display.Top} ({display.Dpi * 100 / 96} %)"));

    // The model name each monitor reports, by the display name Windows gives it. Empty if Windows can't say.
    private static unsafe Dictionary<string, string> Models()
    {
        var models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const QUERY_DISPLAY_CONFIG_FLAGS Active = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;
        if (PInvoke.GetDisplayConfigBufferSizes(Active, out var pathCount, out var modeCount) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return models;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        fixed (DISPLAYCONFIG_PATH_INFO* pathList = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* modeList = modes)
        {
            if (PInvoke.QueryDisplayConfig(Active, &pathCount, pathList, &modeCount, modeList, null) != WIN32_ERROR.ERROR_SUCCESS)
            {
                return models;
            }
        }

        foreach (var path in paths.Take((int)pathCount))
        {
            var source = default(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            source.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;
            var target = default(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;
            if (PInvoke.DisplayConfigGetDeviceInfo(&source.header) == 0 && PInvoke.DisplayConfigGetDeviceInfo(&target.header) == 0)
            {
                var model = target.monitorFriendlyDeviceName.ToString();
                if (model.Length > 0)
                {
                    models[source.viewGdiDeviceName.ToString()] = model;
                }
            }
        }

        return models;
    }
}
