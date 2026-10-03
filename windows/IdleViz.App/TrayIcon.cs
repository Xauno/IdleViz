using System.Diagnostics;
using System.Windows.Input;
using H.NotifyIcon;
using IdleViz.Core;
using Microsoft.UI.Dispatching;

namespace IdleViz.App;

/// <summary>The notification-area icon: left-click opens the flyout, right-click the menu.</summary>
internal sealed partial class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly TaskbarIcon _icon;
    private readonly Stopwatch _sinceFlyoutClosed = new();
    private readonly DispatcherQueueTimer _flashTimer;
    private System.Drawing.Color _color;
    private int _flashStep = IconFlash.Steps;
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
        _flashTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _flashTimer.Interval = TimeSpan.FromMilliseconds(IconFlash.StepMilliseconds);
        _flashTimer.Tick += (_, _) => FlashStep();
        _app.Warnings.Changed += RefreshIcon;
        RefreshIcon();
        _icon.ForceCreate();
    }

    /// <summary>Redraws the icon, for when the taskbar switches between light and dark or a warning comes or goes. Yellow while one is showing.</summary>
    public void RefreshIcon()
    {
        var warnings = _app.Warnings;
        _color = warnings.Any ? TrayGlyph.WarningColor : TrayGlyph.TaskbarColor();
        _icon.ToolTipText = warnings.CurrentTooltip;
        ShowGlyph();
    }

    /// <summary>Swaps to the slashed waveform and back 3 times over about 1 s: an open was refused.</summary>
    public void Flash()
    {
        // A second refusal during a flash starts it again.
        _flashStep = 0;
        ShowGlyph();
        _flashTimer.Start();
    }

    public void Dispose()
    {
        _app.Warnings.Changed -= RefreshIcon;
        _flashTimer.Stop();
        _flyout?.Dismiss();
        _menu?.Dismiss();
        _icon.Dispose();
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

    private void FlashStep()
    {
        _flashStep++;
        if (_flashStep >= IconFlash.Steps)
        {
            _flashTimer.Stop();
        }

        ShowGlyph();
    }

    // A new icon every time: the library disposes the one it had when it is given another.
    private void ShowGlyph() => _icon.Icon = TrayGlyph.Create(_color, IconFlash.IsSlashed(_flashStep));

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
