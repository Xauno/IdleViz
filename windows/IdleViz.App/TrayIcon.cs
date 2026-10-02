using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace IdleViz.App;

/// <summary>The notification-area icon and its right-click menu.</summary>
public sealed partial class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly System.Drawing.Icon _image;

    public TrayIcon(Action exit)
    {
        _image = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "IdleViz.ico"), 16, 16);

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = new MenuCommand(exit) });

        _icon = new TaskbarIcon
        {
            ToolTipText = "IdleViz",
            Icon = _image,
            ContextFlyout = menu,
            // The native Windows menu. The XAML one needs a window to live in, and the app has none.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            NoLeftClickDelay = true,
        };
        _icon.ForceCreate();
    }

    public void Dispose()
    {
        _icon.Dispose();
        _image.Dispose();
    }

    private sealed partial class MenuCommand(Action run) : ICommand
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
