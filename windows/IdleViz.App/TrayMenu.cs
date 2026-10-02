using IdleViz.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IdleViz.App;

/// <summary>
/// The tray icon's right-click menu: Settings, Open visualizer, Exit. A Windows 11 menu needs a
/// window to belong to and the app has none, so an invisible one-pixel window is made at the
/// pointer for as long as the menu is open.
/// </summary>
internal sealed partial class TrayMenu : Window
{
    private readonly Grid _anchor = new();
    private readonly MenuFlyout _menu = new();
    private bool _closing;

    public TrayMenu(App app)
    {
        Content = _anchor;

        var open = new MenuFlyoutItem
        {
            Text = "Open visualizer",
            // Shown for information, as a menu shows a shortcut. The hotkey itself is global.
            KeyboardAcceleratorTextOverride = app.OpenHotkey?.MenuLabel ?? string.Empty,
        };
        var settings = new MenuFlyoutItem { Text = "Settings" };
        var exit = new MenuFlyoutItem { Text = "Exit" };
        settings.Click += (_, _) => app.ShowSettings();
        open.Click += (_, _) => app.OpenVisualizer(TriggerSource.Settings);
        exit.Click += (_, _) => app.Quit();
        _menu.Items.Add(settings);
        _menu.Items.Add(open);
        _menu.Items.Add(new MenuFlyoutSeparator());
        _menu.Items.Add(exit);
        _menu.Closed += (_, _) => CloseOnce();

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // Fully see-through: only the menu, which is a window of its own, is visible.
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)(style | WINDOW_EX_STYLE.WS_EX_LAYERED));
        PInvoke.SetLayeredWindowAttributes(Handle, new COLORREF(0), 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

        _anchor.Loaded += (_, _) => _menu.ShowAt(_anchor, new FlyoutShowOptions { Position = new Windows.Foundation.Point(0, 0) });
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                _menu.Hide();
            }
        };
    }

    private HWND Handle => new(WinRT.Interop.WindowNative.GetWindowHandle(this));

    public void ShowAtPointer()
    {
        PInvoke.GetCursorPos(out var pointer);
        AppWindow.MoveAndResize(new RectInt32(pointer.X, pointer.Y, 1, 1));
        Activate();
        // Focus, so that a click anywhere else closes the menu.
        PInvoke.SetForegroundWindow(Handle);
    }

    /// <summary>Closes the menu if it is still open.</summary>
    public void Dismiss() => CloseOnce();

    // The menu closing and the app quitting can both ask for the close; the window can only close once.
    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }
}
