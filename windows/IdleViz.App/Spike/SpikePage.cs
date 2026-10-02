#pragma warning disable
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Web.WebView2.Core;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App.Spike;

/// <summary>SPIKE: the shared page in WebView2, hosted on a plain window handle, served from idleviz-app://.</summary>
internal sealed class SpikePage
{
    private const string PageCsp = "default-src 'none'; script-src 'self' 'unsafe-eval'; style-src 'self'; font-src 'self'; img-src 'self' data: blob:; connect-src 'self' idleviz-app://presets; frame-src 'self'";
    private const string PluginCsp = "default-src 'none'; script-src idleviz-app:; img-src data: blob:";
    private const string ConverterCsp = "default-src 'none'; script-src 'self' 'unsafe-eval'";

    private readonly string _root;
    private CoreWebView2Environment _environment = null!;
    private CoreWebView2Controller _controller = null!;

    private SpikePage(string root) => _root = root;

    public CoreWebView2 Web => _controller.CoreWebView2;

    public CoreWebView2Environment Environment => _environment;

    public static async Task<SpikePage> CreateAsync(HWND parent, string root, string allowedOrigins)
    {
        var page = new SpikePage(root);
        var clock = Stopwatch.StartNew();
        var options = new CoreWebView2EnvironmentOptions();
        var scheme = new CoreWebView2CustomSchemeRegistration("idleviz-app") { TreatAsSecure = 1, HasAuthorityComponent = true };
        // The projected lists are copies: Add on them changes nothing, so assign whole lists.
        foreach (var origin in allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            scheme.AllowedOrigins.Add(origin);
        }

        options.CustomSchemeRegistrations = new List<CoreWebView2CustomSchemeRegistration> { scheme };
        Log.Info("page", $"Registrations {options.CustomSchemeRegistrations.Count}, origins {scheme.AllowedOrigins.Count}, secure {scheme.TreatAsSecure}, authority {scheme.HasAuthorityComponent}, name {scheme.SchemeName}; types {options.CustomSchemeRegistrations.GetType().FullName} / {typeof(CoreWebView2CustomSchemeRegistration).Assembly.GetName().Name}");
        var dataFolder = Path.Combine(AppPaths.LocalData, "WebView2-spike");
        page._environment = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, dataFolder, options);
        Log.Info("page", $"Environment {page._environment.BrowserVersionString} after {clock.ElapsedMilliseconds} ms, allowed origins \"{allowedOrigins}\"");
        var reference = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)(nint)parent);
        page._controller = await page._environment.CreateCoreWebView2ControllerAsync(reference);
        Log.Info("page", $"Controller after {clock.ElapsedMilliseconds} ms");
        page._controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        page.Resize(parent);
        page._controller.IsVisible = true;

        var web = page.Web;
        web.Settings.AreDevToolsEnabled = true;
        web.AddWebResourceRequestedFilter("idleviz-app://*", CoreWebView2WebResourceContext.All);
        web.NavigationStarting += (_, e) => Log.Info("page", $"Navigation starting {e.Uri}");
        web.WebResourceRequested += page.OnRequest;
        web.NavigationCompleted += (_, e) => Log.Info("page", $"Navigation completed after {clock.ElapsedMilliseconds} ms: success={e.IsSuccess} status={e.HttpStatusCode} error={e.WebErrorStatus}");
        web.ProcessFailed += (_, e) => Log.Info("page", $"Process failed: {e.ProcessFailedKind} {e.Reason}");

        // Console output and CSP violations, through the DevTools protocol.
        web.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, e) => Log.Info("console", Trim(e.ParameterObjectAsJson));
        web.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, e) => Log.Info("exception", Trim(e.ParameterObjectAsJson));
        web.GetDevToolsProtocolEventReceiver("Log.entryAdded").DevToolsProtocolEventReceived += (_, e) => Log.Info("pagelog", Trim(e.ParameterObjectAsJson));
        await web.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        await web.CallDevToolsProtocolMethodAsync("Log.enable", "{}");

        web.Navigate("idleviz-app://app/index.html");
        return page;
    }

    public void Resize(HWND parent)
    {
        PInvoke.GetClientRect(parent, out var rect);
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, rect.right - rect.left, rect.bottom - rect.top);
        Log.Info("page", $"Bounds {rect.right - rect.left} x {rect.bottom - rect.top}, scale {_controller.RasterizationScale}");
    }

    private static string Trim(string text) => text.Length > 700 ? text[..700] + "…" : text;

    private void OnRequest(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            Serve(e);
        }
        catch (Exception error)
        {
            Log.Info("request", $"FAILED {e.Request.Uri}: {error}");
        }
    }

    private void Serve(CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var file = Path.GetFullPath(Path.Combine(_root, relative));
        string origin = "(none)";
        foreach (var header in e.Request.Headers)
        {
            if (header.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase)) { origin = header.Value; }
        }

        if (uri.Host != "app" || !file.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            Log.Info("request", $"404 {e.Request.Uri} ({e.ResourceContext}, origin {origin})");
            e.Response = _environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
            return;
        }

        var bytes = File.ReadAllBytes(file);
        var type = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json",
            ".ttf" => "font/ttf",
            ".png" => "image/png",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
        var headers = $"Content-Type: {type}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: *";
        if (type.StartsWith("text/html", StringComparison.Ordinal))
        {
            var csp = Path.GetFileName(file) switch { "plugin-host.html" => PluginCsp, "converter.html" => ConverterCsp, _ => PageCsp };
            headers += $"\r\nContent-Security-Policy: {csp}";
        }

        Log.Info("request", $"200 {e.Request.Uri} ({e.ResourceContext}, origin {origin}, {bytes.Length} bytes)");
        e.Response = _environment.CreateWebResourceResponse(new MemoryStream(bytes).AsRandomAccessStream(), 200, "OK", headers);
    }
}
