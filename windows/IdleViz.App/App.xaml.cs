using System.Diagnostics.CodeAnalysis;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace IdleViz.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The app object lives as long as the process, and Quit disposes what it owns.")]
public partial class App : Application
{
    private readonly LaunchOptions _launchOptions;
    private readonly SettingsStore _settings = new(AppPaths.SettingsFile);
    private DispatcherQueue? _dispatcher;
    private HotkeyWindow? _hotkeyWindow;
    private VisualizerController? _visualizer;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private Spike.SpikeRunner? _spike;

    public App(LaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
        InitializeComponent();
        // The flyout, the menu and settings are windows that come and go. Closing the last one must not end the app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Log.Info("app", $"Unhandled exception: {e.Exception}");
    }

    /// <summary>The open hotkey as stored, or null if it was cleared.</summary>
    internal Hotkey? OpenHotkey => HotkeySetting.Read(_settings);

    /// <summary>False while Windows refuses the stored hotkey because another app has it.</summary>
    internal bool HotkeyRegistered { get; private set; } = true;

    // The app has no main window: the tray icon is all there is until something opens.
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Log.Info("app", $"Started, version {typeof(App).Assembly.GetName().Version}");

        // SPIKE (W2): its own window and nothing else of the app.
        var spikeArgs = Environment.GetCommandLineArgs();
        if (spikeArgs.Contains("--spike"))
        {
            _spike = new Spike.SpikeRunner(_dispatcher, new VisualizerWindow(), spikeArgs);
            _spike.Start();
            return;
        }

        // The debug switches are ignored in Release builds.
#if DEBUG
        var debug = _launchOptions;
#else
        var debug = new LaunchOptions();
#endif

        // Created now and kept hidden, so the first open is instant.
        _visualizer = new VisualizerController(_dispatcher, dismissEnabled: !debug.NoDismiss);

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += () => OpenVisualizer(TriggerSource.Hotkey);
        _hotkeyWindow.ThemeChanged += () => _trayIcon?.RefreshIcon();
        ApplyHotkey();

        _trayIcon = new TrayIcon(this);

        Program.Relaunched += options => _dispatcher.TryEnqueue(() =>
        {
            try
            {
                OnRelaunched(options);
            }
            catch (Exception error)
            {
                Log.Info("app", $"Relaunch failed: {error}");
                throw;
            }
        });

        if (_launchOptions.Command == UrlCommand.Open)
        {
            OpenVisualizer(TriggerSource.UrlScheme);
        }

        ShowDebugWindows(debug);

        if (debug.OpenAtLaunch)
        {
            var timer = _dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => OpenVisualizer(TriggerSource.Settings);
            timer.Start();
        }
    }

    internal void OpenVisualizer(TriggerSource source) => _visualizer?.Open(source);

    internal void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.BringToFront();
    }

    /// <summary>Stores the hotkey and registers it. Returns false if Windows refuses it.</summary>
    internal bool SetOpenHotkey(Hotkey? hotkey)
    {
        HotkeySetting.Write(_settings, hotkey);
        return ApplyHotkey();
    }

    /// <summary>Lets the hotkey go while the recorder listens, so pressing it there doesn't open the visualizer.</summary>
    internal void SuspendHotkey() => _hotkeyWindow?.Register(null);

    internal bool ApplyHotkey()
    {
        var hotkey = OpenHotkey;
        HotkeyRegistered = _hotkeyWindow?.Register(hotkey) ?? true;
        if (!HotkeyRegistered)
        {
            Log.Info("hotkey", $"Windows refused {hotkey?.Label}: another app has it.");
        }

        return HotkeyRegistered;
    }

    internal void Quit()
    {
        Log.Info("app", "Exit");
        _settingsWindow?.Close();
        _visualizer?.Dispose();
        _hotkeyWindow?.Dispose();
        // Without this the icon stays in the tray until the mouse moves over it.
        _trayIcon?.Dispose();
        _trayIcon = null;
        Exit();
    }

    private void ShowDebugWindows(LaunchOptions debug)
    {
        if (debug.ShowSettings)
        {
            ShowSettings();
        }

        if (debug.ShowFlyout)
        {
            _trayIcon?.ToggleFlyout();
        }

        if (debug.ShowMenu)
        {
            _trayIcon?.ShowMenu();
        }
    }

    private void OnRelaunched(LaunchOptions options)
    {
        if (options.Command == UrlCommand.Open)
        {
            OpenVisualizer(TriggerSource.UrlScheme);
        }
#if DEBUG
        else if (options.ShowSettings || options.ShowFlyout || options.ShowMenu)
        {
            ShowDebugWindows(options);
        }
#endif
        else
        {
            // Started again from the Start menu: there is no window to bring forward, so show settings.
            ShowSettings();
        }
    }
}
