using System.Diagnostics;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>
/// The one web page (visualizer, dim layer and overlay) in WebView2, on the visualizer window. The
/// app talks to it only by running script; the page has no way to call the app: web messages are
/// off, there are no host objects, and every permission is denied. Ported from <c>PageView.swift</c>
/// and <c>AppSchemeHandler.swift</c>.
/// </summary>
internal sealed class PageView : IDisposable
{
    // Ask every 50 ms, for up to 10 s, whether the page's scripts have run.
    private const int ReadyCheckMilliseconds = 50;
    private const int ReadyCheckAttempts = 200;
    private const string ReadyCheck = "['nowPlaying', 'setPresetSettings', 'setCustomPresets'].every((name) => typeof window[name] === 'function')";

    // Audio frames the page hasn't taken yet. More than a couple means it's busy, so newer frames are dropped.
    private const int MaxFramesInFlight = 2;

    private readonly HWND _parent;
    private readonly DispatcherQueue _dispatcher;
    private readonly string _root;
    private readonly string _presetsRoot;
    private readonly DispatcherQueueTimer _readyTimer;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly PageWatchdog _watchdog = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TaskCompletionSource<CoreWebView2Environment?> _environmentReady = new();
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;

    // The controller's web view, kept for as long as the controller. The page's event handlers are
    // registered on it, and if nothing refers to it the garbage collector frees it while WebView2
    // still calls those handlers: the app crashed on the first request, or the page never got ready.
    private CoreWebView2? _web;
    private bool _visible;
    private bool _loaded;
    private int _readyAttempts;
    private bool _disposed;

    // Counts page loads, so an answer from an earlier page is ignored.
    private int _loadId;
    private int _framesInFlight;
    private PageStatus? _lastStatus;

    // The page's own counts at the first answer after the window opened, for the line logged when it closes.
    private long _framesAtOpen = -1;
    private long _audioFramesAtOpen;

    /// <summary>The latest <c>nowPlaying</c> call, replayed whenever the page (re)loads.</summary>
    private string _nowPlayingScript = OverlayPayload.Script(null);

    /// <summary>The latest list of custom presets and hung presets, replayed the same way.</summary>
    private string _customPresetsScript = CustomPresetPayload.Empty.Script;

    /// <summary>The latest preset controls, replayed the same way.</summary>
    private string _presetSettingsScript = new PresetSettings().Script;

    /// <summary>The latest audio delay, replayed the same way. The progress bar needs it to show the position you hear.</summary>
    private string _audioDelayScript = AudioDelaySetting.Script(0);
    private string _brightnessScript = BrightnessSetting.Script(BrightnessSetting.DefaultValue);
    private string _overlayEnabledScript = OverlaySetting.Script(true);

    /// <param name="parent">The visualizer window. The page fills it.</param>
    /// <param name="dispatcher">The UI thread's queue.</param>
    public PageView(HWND parent, DispatcherQueue dispatcher)
    {
        _parent = parent;
        _dispatcher = dispatcher;
        _root = Path.Combine(AppContext.BaseDirectory, "web");
        _presetsRoot = AppPaths.PresetsFolder;
        _readyTimer = dispatcher.CreateTimer();
        _readyTimer.Interval = TimeSpan.FromMilliseconds(ReadyCheckMilliseconds);
        _readyTimer.Tick += (_, _) => CheckReady();
        _statusTimer = dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(1);
        _statusTimer.Tick += (_, _) => CheckStatus();
    }

    /// <summary>Called with the presets the page reports as failed, each time that list changes.</summary>
    public event Action<IReadOnlyList<PresetFailure>>? FailuresChanged;

    /// <summary>Called with the page's preset list each time the page has loaded.</summary>
    public event Action<IReadOnlyList<PresetInfo>>? PresetsLoaded;

    /// <summary>Called when a different preset comes on screen.</summary>
    public event Action<string>? PresetShown;

    /// <summary>Called with the preset that was on screen when the page stopped answering.</summary>
    public event Action<string>? Hung;

    /// <summary>The WebView2 environment, once it exists; null if WebView2 couldn't start. The converter page shares it.</summary>
    public Task<CoreWebView2Environment?> Environment => _environmentReady.Task;

    /// <summary>The visualizer window, which the page (and the hidden converter page) live in.</summary>
    public HWND Parent => _parent;

    /// <summary>Starts WebView2 and loads the page. The window may still be hidden.</summary>
    public async void Start()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                // No autoplay prompt for the AudioContext the visualizer makes.
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
            };
            var dataFolder = Path.Combine(AppPaths.LocalData, "WebView2");
            _environment = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, dataFolder, options);
            _environmentReady.TrySetResult(_environment);
            if (_disposed)
            {
                return;
            }

            Log.Info("page", $"WebView2 {_environment.BrowserVersionString}");
            await CreateController();
        }
        catch (Exception error)
        {
            Log.Info("page", $"Can't start the page; the visualizer stays black. Is the WebView2 runtime installed? {error.Message}");
            _environmentReady.TrySetResult(null);
        }
    }

    /// <summary>Shows a Spotify item on the overlay, or <c>null</c> for no track.</summary>
    public void Show(OverlayPayload? payload)
    {
        _nowPlayingScript = OverlayPayload.Script(payload);
        Run(_nowPlayingScript);
    }

    /// <summary>Hands the bundled plugins and the custom presets folder to the page, and asks for the new preset list.</summary>
    public void SendCustomPresets(CustomPresetPayload payload)
    {
        _customPresetsScript = payload.Script;
        if (_loaded)
        {
            Run(_customPresetsScript);
            FetchPresetList();
        }
    }

    /// <summary>Hands the preset controls to the page, which applies them at once.</summary>
    public void SendPresetSettings(PresetSettings settings)
    {
        _presetSettingsScript = settings.Script;
        Run(_presetSettingsScript);
    }

    /// <summary>Tells the page how far the speakers lag behind Spotify, in seconds.</summary>
    public void SendAudioDelay(double seconds)
    {
        _audioDelayScript = AudioDelaySetting.Script(seconds);
        Run(_audioDelayScript);
    }

    /// <summary>Sets how much of the visualizer shows through the black dim layer, 0.5 to 1.</summary>
    public void SendBrightness(double value)
    {
        _brightnessScript = BrightnessSetting.Script(value);
        Run(_brightnessScript);
    }

    /// <summary>Shows or hides the Spotify overlay as a whole.</summary>
    public void SendOverlayEnabled(bool enabled)
    {
        _overlayEnabledScript = OverlaySetting.Script(enabled);
        Run(_overlayEnabledScript);
    }

    /// <summary>Asks the page for the next preset. It does nothing outside Shuffle.</summary>
    public void SkipPreset() => Run(VisualizerKeys.SkipScript);

    /// <summary>Shows the heart that confirms the like key.</summary>
    public void ShowLike(bool liked) => Run(VisualizerKeys.LikeScript(liked));

    /// <summary>The preset on screen right now, or null if the page doesn't say. The once-a-second status can be a moment behind.</summary>
    public async Task<string?> CurrentPreset()
    {
        if (!_loaded || _controller is null)
        {
            return null;
        }

        try
        {
            return PageStatus.FromReply(await _controller.CoreWebView2.ExecuteScriptAsync("window.idlevizStatus?.()"))?.Preset;
        }
        catch (Exception error)
        {
            Log.Info("page", $"Asking the page for its preset failed: {error.Message}");
            return null;
        }
    }

    /// <summary>Hands one audio frame's script to the page. Frames are dropped, not queued, while the page is busy or loading.</summary>
    public async void SendAudioFrame(string script)
    {
        if (!_loaded || _controller is null || _framesInFlight >= MaxFramesInFlight)
        {
            return;
        }

        var load = _loadId;
        _framesInFlight++;
        try
        {
            await _controller.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception)
        {
            // The page went away mid-call (reload or crash); the next frame goes to the new one.
        }
        finally
        {
            if (load == _loadId)
            {
                _framesInFlight--;
            }
        }
    }

    /// <summary>
    /// While the window is open, asks the page once a second how it's doing. A page that stops
    /// answering (a preset or plugin stuck in a loop) is replaced.
    /// </summary>
    public void StartStatusChecks()
    {
        if (_statusTimer.IsRunning)
        {
            return;
        }

        _watchdog.Start(Now);
        _framesAtOpen = -1;
        _statusTimer.Start();
    }

    public void StopStatusChecks()
    {
        _statusTimer.Stop();
        _watchdog.Stop();
        if (_framesAtOpen >= 0 && _lastStatus is { } status)
        {
            Log.Info("page", $"While open: the page drew {status.Frames - _framesAtOpen} frames and got {status.AudioFrames - _audioFramesAtOpen} audio frames");
        }

        _framesAtOpen = -1;
    }

#if DEBUG
    /// <summary>Debug builds only: makes the page's renderer loop forever, as a broken preset would.</summary>
    public void Hang()
    {
        Log.Info("page", "Hanging the page on purpose (--hang-page)");
        Run("setTimeout(() => { for (;;) {} }, 0)");
    }
#endif

    /// <summary>
    /// The page renders only while the window is showing. Hidden, it is suspended, so it costs
    /// nothing between opens.
    /// </summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_controller is null)
        {
            return;
        }

        if (visible)
        {
            Resize();
        }

        _controller.IsVisible = visible;
        if (!visible)
        {
            Suspend();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _readyTimer.Stop();
        _statusTimer.Stop();
        _controller?.Close();
        _controller = null;
        _web = null;
    }

    private async Task CreateController()
    {
        if (_environment is null)
        {
            return;
        }

        var reference = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)(nint)_parent);
        var options = _environment.CreateCoreWebView2ControllerOptions();
        // Like the Mac's non-persistent store: nothing the page or a plugin stores outlives the page.
        options.IsInPrivateModeEnabled = true;
        var controller = await _environment.CreateCoreWebView2ControllerAsync(reference, options);
        if (_disposed)
        {
            controller.Close();
            return;
        }

        _controller = controller;
        _web = controller.CoreWebView2;
        controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        Resize();
        controller.IsVisible = _visible;

        var web = _web;
        var settings = web.Settings;
        settings.IsWebMessageEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif

        web.AddWebResourceRequestedFilter($"{AppAddresses.AppOrigin}/*", CoreWebView2WebResourceContext.All);
        web.AddWebResourceRequestedFilter($"{AppAddresses.PresetsOrigin}/*", CoreWebView2WebResourceContext.All);
        web.WebResourceRequested += (_, e) => WebServer.Respond(_environment, e, _root, _presetsRoot);
        web.NavigationStarting += (_, e) => e.Cancel = !AllowNavigation(e.Uri, "page");
        web.FrameNavigationStarting += (_, e) => e.Cancel = !AllowNavigation(e.Uri, "frame");
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Log.Info("page", $"Blocked a new window for {e.Uri}");
        };
        web.PermissionRequested += (_, e) =>
        {
            // The visuals only ever see Spotify's audio, which reaches the page from the app.
            e.State = CoreWebView2PermissionState.Deny;
            Log.Info("page", $"Denied the permission {e.PermissionKind}");
        };
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                Log.Info("page", $"The page didn't load: {e.WebErrorStatus}");
                return;
            }

            _readyAttempts = 0;
            _readyTimer.Start();
        };
        web.ProcessFailed += OnProcessFailed;
        Load();
    }

    private void Load()
    {
        ResetPageState();
        _readyTimer.Stop();
        _controller?.CoreWebView2.Navigate(AppAddresses.PageUrl);
    }

    private static bool AllowNavigation(string url, string kind)
    {
        // A frame starts out as about:blank before it loads its own address.
        if (AppAddresses.IsAppPage(url) || (kind == "frame" && url == "about:blank"))
        {
            return true;
        }

        Log.Info("page", $"Blocked {kind} navigation to {url}");
        return false;
    }

    // WebView2 can report the navigation as complete before the page's modules have run, and a call
    // made then is lost (seen on the Mac). So ask whether its functions exist, and only then send state.
    private async void CheckReady()
    {
        if (_controller is null || _loaded)
        {
            _readyTimer.Stop();
            return;
        }

        if (++_readyAttempts > ReadyCheckAttempts)
        {
            _readyTimer.Stop();
            Log.Info("page", $"The page's scripts didn't start within {ReadyCheckAttempts * ReadyCheckMilliseconds / 1000} s");
            return;
        }

        try
        {
            if (await _controller.CoreWebView2.ExecuteScriptAsync(ReadyCheck) == "true" && !_loaded)
            {
                _readyTimer.Stop();
                _loaded = true;
                Log.Info("page", "Ready");
                Run(_customPresetsScript);
                Run(_presetSettingsScript);
                Run(_audioDelayScript);
                Run(_brightnessScript);
                Run(_overlayEnabledScript);
                Run(_nowPlayingScript);
                FetchPresetList();
            }
        }
        catch (Exception error)
        {
            Log.Info("page", $"Asking the page whether it is ready failed: {error.Message}");
        }
    }

    private async void FetchPresetList()
    {
        if (_controller is null)
        {
            return;
        }

        var load = _loadId;
        try
        {
            var reply = await _controller.CoreWebView2.ExecuteScriptAsync("window.idlevizPresets?.()");
            // A page replaced in the meantime answers for nothing; the new one is asked when it is ready.
            if (load != _loadId)
            {
                return;
            }

            var presets = PresetInfo.List(reply);
            Log.Info("page", $"{presets.Count} presets");
            PresetsLoaded?.Invoke(presets);
        }
        catch (Exception error)
        {
            Log.Info("page", $"Asking the page for its presets failed: {error.Message}");
        }
    }

    private async void Run(string script)
    {
        if (!_loaded || _controller is null)
        {
            return;
        }

        try
        {
            await _controller.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception error)
        {
            Log.Info("page", $"A call to the page failed: {error.Message}");
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Info("page", $"WebView2 process failed: {e.ProcessFailedKind} ({e.Reason}, exit code {e.ExitCode})");
        _dispatcher.TryEnqueue(async () =>
        {
            if (_disposed || !IsCurrent(sender))
            {
                return;
            }

            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                // Everything is gone: start over with a new controller.
                _controller?.Close();
                _controller = null;
                _web = null;
                ResetPageState();
                try
                {
                    await CreateController();
                }
                catch (Exception error)
                {
                    Log.Info("page", $"Can't restart the page: {error.Message}");
                }
            }
            else if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                // Load again, and the state is sent again once it is ready.
                Load();
            }
        });
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    private bool IsCurrent(CoreWebView2 sender)
    {
        try
        {
            return _controller?.CoreWebView2 == sender;
        }
        catch (Exception)
        {
            // The controller can't be asked once its browser process is gone; that report is for it.
            return true;
        }
    }

    private void ResetPageState()
    {
        _loaded = false;
        _loadId++;
        _framesInFlight = 0;
        _lastStatus = null;
        _framesAtOpen = -1;
    }

    private async void CheckStatus()
    {
        if (_controller is null)
        {
            return;
        }

        if (_watchdog.ShouldReload(Now) && _environment is not null)
        {
            var preset = _lastStatus?.Preset;
            Log.Info("page", $"The page stopped answering; replacing it (last preset: {preset ?? "none"})");
            // Whatever was on screen is the likely cause. Keep it out, or the new page would hang on it too.
            // The library answers at once with a new list, which the new page gets when it is ready.
            if (preset is not null)
            {
                Hung?.Invoke(preset);
            }

            await ReplaceStuckPage();
            return;
        }

        if (!_loaded)
        {
            return;
        }

        var load = _loadId;
        string reply;
        try
        {
            reply = await _controller.CoreWebView2.ExecuteScriptAsync("window.idlevizStatus?.()");
        }
        catch (Exception)
        {
            return;
        }

        if (load != _loadId || PageStatus.FromReply(reply) is not { } status)
        {
            return;
        }

        _watchdog.Replied(Now);
        if (status.Preset != _lastStatus?.Preset)
        {
            Log.Info("page", $"Preset: {status.Preset ?? "none"}");
            if (status.Preset is { } shown)
            {
                PresetShown?.Invoke(shown);
            }
        }

        if (!status.SameFailures(_lastStatus) && (_lastStatus is not null || status.Failed.Count > 0))
        {
            var known = (_lastStatus?.Failed ?? []).Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var failure in status.Failed.Where(f => !known.Contains(f.Id)))
            {
                Log.Info("page", $"Failed to load {failure.Id}: {failure.Error}");
            }

            FailuresChanged?.Invoke(status.Failed);
        }

        if (_framesAtOpen < 0)
        {
            _framesAtOpen = status.Frames;
            _audioFramesAtOpen = status.AudioFrames;
        }

        _lastStatus = status;
    }

    /// <summary>
    /// A renderer stuck in a JavaScript loop can't be reloaded, and ending it doesn't make WebView2
    /// report a crash (runtime 124): navigating that WebView again crashed the app. So, as on the Mac,
    /// the stuck renderer is ended (or it would keep a core busy) and a new WebView takes the old one's place.
    /// </summary>
    private async Task ReplaceStuckPage()
    {
        var ended = 0;
        try
        {
            // The environment belongs to IdleViz alone, so every renderer in it is the page's or a plugin frame's.
            foreach (var info in _environment?.GetProcessInfos() ?? [])
            {
                if (info.Kind != CoreWebView2ProcessKind.Renderer)
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(info.ProcessId);
                    process.Kill();
                    ended++;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
            }
        }
        catch (Exception error)
        {
            Log.Info("page", $"Listing the page's processes failed: {error.Message}");
        }

        Log.Info("page", $"Ended {ended} renderer process(es)");
        ResetPageState();
        _readyTimer.Stop();
        var old = _controller;
        _controller = null;
        _web = null;
        try
        {
            old?.Close();
            await CreateController();
        }
        catch (Exception error)
        {
            Log.Info("page", $"Can't make a new page: {error.Message}");
        }
    }

    private void Resize()
    {
        if (_controller is null)
        {
            return;
        }

        PInvoke.GetClientRect(_parent, out var rect);
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, rect.right - rect.left, rect.bottom - rect.top);
    }

    private async void Suspend()
    {
        try
        {
            if (_controller is { IsVisible: false } controller)
            {
                await controller.CoreWebView2.TrySuspendAsync();
            }
        }
        catch (Exception error)
        {
            // Suspending is a saving, not a must: a page that wasn't suspended is only hidden.
            Log.Info("page", $"Couldn't suspend the page: {error.Message}");
        }
    }
}
