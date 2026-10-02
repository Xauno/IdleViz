using IdleViz.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>
/// The settings window: small, portrait, fixed size, one page that scrolls. Closing it only closes
/// the window; the app keeps running in the tray.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const double WidthInPixels = 440;
    private const double HeightInPixels = 680;

    private readonly App _app;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();

        Title = "IdleViz Settings";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "IdleViz.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        var scale = PInvoke.GetDpiForWindow(Handle) / 96.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(WidthInPixels * scale), (int)Math.Round(HeightInPixels * scale)));

        Recorder.Attach(_app);
        Recorder.HotkeyChanged += ShowHotkeyState;
        ShowHotkeyState();
    }

    private HWND Handle => new(WinRT.Interop.WindowNative.GetWindowHandle(this));

    /// <summary>Shows the window, or brings it forward if it is already open.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        PInvoke.SetForegroundWindow(Handle);
    }

    private void ShowHotkeyState()
    {
        if (_app.HotkeyRegistered)
        {
            HotkeyCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
        }
        else
        {
            HotkeyCard.Description = "Another app is already using this shortcut. Pick a different one.";
        }
    }

    private void OnOpenNowClick(object sender, RoutedEventArgs e) => _app.OpenVisualizer(TriggerSource.Settings);
}
