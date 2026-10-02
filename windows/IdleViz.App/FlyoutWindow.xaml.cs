using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace IdleViz.App;

/// <summary>
/// The small flyout a left-click on the tray icon opens: a Settings row, and the open hotkey for
/// information. It closes when it loses focus or on Esc, like the Windows volume flyout.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const double WidthInPixels = 290;
    private const double MarginInPixels = 12;

    private readonly App _app;
    private bool _closing;

    public FlyoutWindow(App app)
    {
        _app = app;
        InitializeComponent();

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        RoundCorners();

        // Read each time the flyout opens.
        HotkeyText.Text = _app.OpenHotkey?.Label ?? "Not set";

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                CloseOnce();
            }
        };
        Root.KeyDown += OnKeyDown;
    }

    private HWND Handle => new(WinRT.Interop.WindowNative.GetWindowHandle(this));

    /// <summary>Sizes the window to its rows and shows it in the corner of the screen where the tray is.</summary>
    public void ShowAboveTray()
    {
        var scale = PInvoke.GetDpiForWindow(Handle) / 96.0;
        Root.Measure(new Windows.Foundation.Size(WidthInPixels, double.PositiveInfinity));
        var width = (int)Math.Ceiling(WidthInPixels * scale);
        var height = (int)Math.Ceiling(Root.DesiredSize.Height * scale);
        var margin = (int)Math.Round(MarginInPixels * scale);

        // The work area leaves the taskbar out, so this lands just above it.
        var workArea = DisplayArea.Primary.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + workArea.Width - width - margin,
            workArea.Y + workArea.Height - height - margin,
            width,
            height));

        Activate();
        PInvoke.SetForegroundWindow(Handle);
        SettingsRow.Focus(FocusState.Programmatic);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        CloseOnce();
        _app.ShowSettings();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseOnce();
        }
    }

    /// <summary>Closes the flyout if it is still open.</summary>
    public void Dismiss() => CloseOnce();

    // Losing focus and the Settings click can both ask for the close; the window can only close once.
    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }

    private unsafe void RoundCorners()
    {
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(
            Handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }
}
