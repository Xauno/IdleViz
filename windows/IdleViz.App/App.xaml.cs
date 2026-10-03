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
    private SpotifyInfo? _spotify;
    private OverlayFeed? _overlay;
    private DispatcherQueueTimer? _openAtLaunchTimer;
    private DispatcherQueueTimer? _firstReadingTimer;
    private TriggerSource? _waitingSource;

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

        _spotify = new SpotifyInfo(_dispatcher);
        _spotify.Start();

        _overlay = new OverlayFeed(_spotify, _visualizer.Page, _dispatcher);
        _visualizer.Opened += _overlay.Start;
        _visualizer.Closed += _overlay.Stop;
        _visualizer.Start();

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
            // In a field: a timer nothing refers to is collected, and then never fires.
            _openAtLaunchTimer = _dispatcher.CreateTimer();
            _openAtLaunchTimer.Interval = TimeSpan.FromSeconds(3);
            _openAtLaunchTimer.IsRepeating = false;
            _openAtLaunchTimer.Tick += (_, _) => OpenVisualizer(TriggerSource.Settings);
            _openAtLaunchTimer.Start();
        }
    }

    /// <summary>Opens the visualizer if the open rules allow it. A refused manual trigger flashes the tray icon.</summary>
    internal void OpenVisualizer(TriggerSource source)
    {
        if (_visualizer is null || _spotify is null)
        {
            return;
        }

        // Already open: the window decides. In the no-dismiss mode a second trigger closes it.
        if (_visualizer.IsOpen)
        {
            _visualizer.Open(source);
            return;
        }

        var tracker = _spotify.Tracker;
        switch (OpenRules.Refusal(tracker, DateTimeOffset.Now))
        {
            case OpenRefusal.NotKnownYet:
                WaitForFirstReading(source);
                return;
            case { } refusal:
                Refuse(source, refusal);
                return;
        }

        if (tracker.Current is null)
        {
            Log.Info("open", "Spotify is between two tracks; opening anyway");
        }

        _visualizer.Open(source);
    }

    // The app has only just started and Windows hasn't said what Spotify is doing yet.
    private void WaitForFirstReading(TriggerSource source)
    {
        // A manual trigger during the wait makes it manual, so a refusal still flashes the icon.
        if (_waitingSource is null || source.IsManual())
        {
            _waitingSource = source;
        }

        if (_firstReadingTimer is not null || _dispatcher is null || _spotify is null)
        {
            return;
        }

        Log.Info("open", $"Waiting for Spotify's state before opening via {source}");
        _firstReadingTimer = _dispatcher.CreateTimer();
        _firstReadingTimer.Interval = TimeSpan.FromSeconds(OpenRules.FirstReadingWaitSeconds);
        _firstReadingTimer.IsRepeating = false;
        _firstReadingTimer.Tick += (_, _) =>
        {
            if (StopWaiting() is { } waiting)
            {
                Refuse(waiting, OpenRefusal.NotKnownYet);
            }
        };
        _spotify.Tracker.Changed += OnFirstReading;
        _firstReadingTimer.Start();
    }

    private void OnFirstReading(NowPlaying? item)
    {
        if (StopWaiting() is { } waiting)
        {
            OpenVisualizer(waiting);
        }
    }

    /// <summary>Ends a wait for the first reading and returns the trigger that was waiting, if any.</summary>
    private TriggerSource? StopWaiting()
    {
        var waiting = _waitingSource;
        _waitingSource = null;
        _firstReadingTimer?.Stop();
        _firstReadingTimer = null;
        if (_spotify is not null)
        {
            _spotify.Tracker.Changed -= OnFirstReading;
        }

        return waiting;
    }

    private void Refuse(TriggerSource source, OpenRefusal refusal)
    {
        var reason = refusal switch
        {
            OpenRefusal.SpotifyNotRunning => "Spotify isn't running, or has played nothing since it started",
            OpenRefusal.NoTrack => "Spotify has no track",
            _ => $"Windows hasn't said what Spotify is doing after {OpenRules.FirstReadingWaitSeconds} s",
        };
        Log.Info("open", $"Not opening via {source}: {reason}");
        // A refused idle trigger does nothing visible.
        if (source.IsManual())
        {
            _trayIcon?.Flash();
        }
    }

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
        StopWaiting();
        _spotify?.Dispose();
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
