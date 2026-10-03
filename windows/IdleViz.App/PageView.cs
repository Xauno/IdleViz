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

    private readonly HWND _parent;
    private readonly DispatcherQueue _dispatcher;
    private readonly string _root;
    private readonly string _presetsRoot;
    private readonly DispatcherQueueTimer _readyTimer;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private bool _visible;
    private bool _loaded;
    private int _readyAttempts;
    private bool _disposed;

    /// <summary>The latest <c>nowPlaying</c> call, replayed whenever the page (re)loads.</summary>
    private string _nowPlayingScript = OverlayPayload.Script(null);

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
    }

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
        }
    }

    /// <summary>Shows a Spotify item on the overlay, or <c>null</c> for no track.</summary>
    public void Show(OverlayPayload? payload)
    {
        _nowPlayingScript = OverlayPayload.Script(payload);
        Run(_nowPlayingScript);
    }

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
        _controller?.Close();
        _controller = null;
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
        controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        Resize();
        controller.IsVisible = _visible;

        var web = controller.CoreWebView2;
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
        web.WebResourceRequested += OnRequest;
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
        _loaded = false;
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
                Run(_nowPlayingScript);
            }
        }
        catch (Exception error)
        {
            Log.Info("page", $"Asking the page whether it is ready failed: {error.Message}");
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
            if (_disposed)
            {
                return;
            }

            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                // Everything is gone: start over with a new controller.
                _controller?.Close();
                _controller = null;
                _loaded = false;
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

    // Answers the app's two addresses from the web folder and the presets folder. A handler that
    // throws makes the request fail with no other sign (W2 spike), so nothing may escape it.
    private void OnRequest(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_environment is null)
        {
            return;
        }

        var url = e.Request.Uri;
        try
        {
            var file = AppAddresses.FileFor(url, _root) ?? AppAddresses.PresetFile(url, _presetsRoot);
            if (file is null)
            {
                // The browser asks for a favicon on every load; only other misses are worth a line.
                if (!url.EndsWith("/favicon.ico", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info("page", $"404 {url}");
                }

                e.Response = _environment.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
                return;
            }

            var bytes = File.ReadAllBytes(file);
            var type = AppAddresses.MimeType(file);
            // A plugin's sandboxed frame has an opaque origin, so every script it loads is a cross-origin request.
            var headers = $"Content-Type: {type}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: *";
            if (type.StartsWith("text/html", StringComparison.Ordinal))
            {
                headers += $"\r\nContent-Security-Policy: {AppAddresses.ContentSecurityPolicyFor(file)}";
            }

            e.Response = _environment.CreateWebResourceResponse(new MemoryStream(bytes).AsRandomAccessStream(), 200, "OK", headers);
        }
        catch (Exception error)
        {
            Log.Info("page", $"Serving {url} failed: {error.Message}");
            try
            {
                e.Response = _environment.CreateWebResourceResponse(null, 500, "Internal Server Error", string.Empty);
            }
            catch (Exception)
            {
                // Nothing more to do; the request fails.
            }
        }
    }
}
