using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// The borderless black window that covers the primary display, above everything including the
/// taskbar. It is created once at launch and hidden between opens, so opening is instant.
/// </summary>
internal sealed class VisualizerWindow() : NativeWindow(
    "IdleViz visualizer",
    // Layered, so the whole window can fade. Tool window, so it has no taskbar button and isn't in Alt+Tab.
    WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_LAYERED,
    WINDOW_STYLE.WS_POPUP)
{
    /// <summary>While true, the mouse pointer is invisible over the window.</summary>
    public bool HidesCursor { get; set; }

    /// <summary>Shows the window over the primary display, on top, without taking focus.</summary>
    public void Show()
    {
        var bounds = PrimaryDisplayBounds();
        PInvoke.SetWindowPos(
            Handle,
            new HWND(-1), // HWND_TOPMOST
            bounds.left,
            bounds.top,
            bounds.right - bounds.left,
            bounds.bottom - bounds.top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    public void Hide() => PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>Asks Windows for keyboard focus. Windows may refuse when nobody triggered the open by hand.</summary>
    public bool TakeFocus() => PInvoke.SetForegroundWindow(Handle);

    /// <summary>0 is invisible, 1 is solid.</summary>
    public void SetOpacity(double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        PInvoke.SetLayeredWindowAttributes(Handle, new COLORREF(0), alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
    }

    /// <summary>While true, clicks go through to whatever is underneath, as during a fade-out.</summary>
    public void SetClickThrough(bool clickThrough)
    {
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style = clickThrough ? style | WINDOW_EX_STYLE.WS_EX_TRANSPARENT : style & ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)style);
    }

    protected override LRESULT? OnMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == PInvoke.WM_SETCURSOR && HidesCursor)
        {
            PInvoke.SetCursor(HCURSOR.Null);
            return (LRESULT)1;
        }

        return null;
    }

    private static RECT PrimaryDisplayBounds()
    {
        var monitor = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(monitor, ref info);
        return info.rcMonitor;
    }
}
