using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace IdleViz.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The app object lives as long as the process, and Quit disposes the tray icon.")]
public partial class App : Application
{
    private TrayIcon? _trayIcon;

    public App()
    {
        InitializeComponent();
    }

    // The app has no window of its own: the tray icon is all there is until something opens.
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _trayIcon = new TrayIcon(exit: Quit);
    }

    private void Quit()
    {
        // Without this the icon stays in the tray until the mouse moves over it.
        _trayIcon?.Dispose();
        _trayIcon = null;
        Exit();
    }
}
