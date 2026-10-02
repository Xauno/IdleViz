using System.Diagnostics;
using System.Windows.Input;
using H.NotifyIcon;

namespace IdleViz.App;

/// <summary>The notification-area icon: left-click opens the flyout, right-click the menu.</summary>
internal sealed partial class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly TaskbarIcon _icon;
    private readonly Stopwatch _sinceFlyoutClosed = new();
    private System.Drawing.Icon? _image;
    private FlyoutWindow? _flyout;
    private TrayMenu? _menu;

    public TrayIcon(App app)
    {
        _app = app;
        _icon = new TaskbarIcon
        {
            ToolTipText = "IdleViz",
            NoLeftClickDelay = true,
            LeftClickCommand = new ClickCommand(ToggleFlyout),
            // The library's own menu is not used. Its Windows 11 style one keeps a hidden window that
            // takes keyboard focus when the app starts, so TrayMenu builds one only when it is asked for.
            RightClickCommand = new ClickCommand(ShowMenu),
        };
        RefreshIcon();
        _icon.ForceCreate();
    }

    /// <summary>Redraws the icon, for when the taskbar switches between light and dark.</summary>
    public void RefreshIcon()
    {
        var previous = _image;
        _image = TrayGlyph.Create(TrayGlyph.TaskbarColor());
        _icon.Icon = _image;
        previous?.Dispose();
    }

    public void Dispose()
    {
        _flyout?.Dismiss();
        _menu?.Dismiss();
        _icon.Dispose();
        _image?.Dispose();
    }

    public void ToggleFlyout()
    {
        // The click that should close an open flyout takes its focus away first, which closes it.
        // Without this check that same click would open it again.
        if (_flyout is not null || (_sinceFlyoutClosed.IsRunning && _sinceFlyoutClosed.ElapsedMilliseconds < 300))
        {
            return;
        }

        _flyout = new FlyoutWindow(_app);
        _flyout.Closed += (_, _) =>
        {
            _flyout = null;
            _sinceFlyoutClosed.Restart();
        };
        _flyout.ShowAboveTray();
    }

    public void ShowMenu()
    {
        _menu?.Dismiss();
        _menu = new TrayMenu(_app);
        _menu.Closed += (_, _) => _menu = null;
        _menu.ShowAtPointer();
    }

    private sealed partial class ClickCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => run();
    }
}
