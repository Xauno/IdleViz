using IdleViz.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace IdleViz.App;

/// <summary>
/// The small flyout a left-click on the tray icon opens: a Settings row, the open hotkey for
/// information, and one row per warning. It closes when it loses focus or on Esc, like the
/// Windows volume flyout.
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

        // The warnings are checked again each time too. The answers come a moment later.
        ShowWarnings();
        _app.Warnings.Changed += OnWarningsChanged;
        Closed += (_, _) => _app.Warnings.Changed -= OnWarningsChanged;
        _app.RecheckWarnings();

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                CloseOnce();
            }
        };
        Root.KeyDown += OnKeyDown;
        // Rows made in code only measure at their full height once they are in a shown window.
        Root.Loaded += (_, _) => Place();
    }

    private HWND Handle => new(WinRT.Interop.WindowNative.GetWindowHandle(this));

    /// <summary>Sizes the window to its rows and shows it in the corner of the screen where the tray is.</summary>
    public void ShowAboveTray()
    {
        Place();
        Activate();
        PInvoke.SetForegroundWindow(Handle);
        SettingsRow.Focus(FocusState.Programmatic);
    }

    private void Place()
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
    }

    // A row came or went while the flyout is open: it grows or shrinks upwards.
    private void OnWarningsChanged()
    {
        if (_closing)
        {
            return;
        }

        ShowWarnings();
        Place();
    }

    private void ShowWarnings()
    {
        WarningRows.Children.Clear();
        foreach (var warning in _app.Warnings.Current)
        {
            WarningRows.Children.Add(WarningRow(warning));
        }
    }

    private Button WarningRow(Warning warning)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = warning.Title, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = "Show details \u203A",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE7BA", // Warning
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF5, 0xB8, 0x00)),
        });
        content.Children.Add(text);

        var row = new Button
        {
            Content = content,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(4),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, warning.Title);
        row.Click += (_, _) =>
        {
            CloseOnce();
            _app.ShowWarning(warning);
        };
        return row;
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
