#pragma warning disable
// SPIKE: the page from idleviz-app:// with the plain .NET WebView2 API, where AllowedOrigins can be set.
// Usage: Spike.Scheme.exe <web root> <allowed origins, comma separated, or "none"> <seconds> <log file>
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

internal static class Program
{
    private const string PageCsp = "default-src 'none'; script-src 'self' 'unsafe-eval'; style-src 'self'; font-src 'self'; img-src 'self' data: blob:; connect-src 'self' idleviz-app://presets; frame-src 'self'";
    private const string PluginCsp = "default-src 'none'; script-src idleviz-app:; img-src data: blob:";
    private static string s_log = "";

    private static void Log(string text) => File.AppendAllText(s_log, $"{DateTime.Now:HH:mm:ss.fff} {text}\n");

    [STAThread]
    private static void Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        var origins = args[1] == "none" ? new List<string>() : args[1].Split(',').ToList();
        var seconds = int.Parse(args[2]);
        s_log = args[3];
        var https = args.Length > 4 && args[4] == "https";
        var appBase = https ? "https://app.idleviz.invalid/" : "idleviz-app://app/";
        var pluginCsp = https ? "default-src 'none'; script-src https://app.idleviz.invalid https://presets.idleviz.invalid; img-src data: blob:" : PluginCsp;
        var pageCsp = https ? PageCsp.Replace("idleviz-app://presets", "https://presets.idleviz.invalid") : PageCsp;
        Log($"START origins=[{string.Join(" ", origins)}]");
        ApplicationConfiguration.Initialize();
        var form = new Form { Width = 900, Height = 600, Text = "scheme spike", StartPosition = FormStartPosition.Manual, Left = 100, Top = 100, ShowInTaskbar = false };
        form.Shown += async (_, _) =>
        {
            try
            {
                var scheme = new CoreWebView2CustomSchemeRegistration("idleviz-app") { TreatAsSecure = true, HasAuthorityComponent = true };
                foreach (var origin in origins) { scheme.AllowedOrigins.Add(origin); }
                var options = new CoreWebView2EnvironmentOptions(null, null, null, false, new List<CoreWebView2CustomSchemeRegistration> { scheme });
                var data = Path.Combine(Path.GetTempPath(), "idleviz-scheme-spike-" + Guid.NewGuid().ToString("N")[..8]);
                var environment = await CoreWebView2Environment.CreateAsync(null, data, options);
                var controller = await environment.CreateCoreWebView2ControllerAsync(form.Handle);
                controller.Bounds = form.ClientRectangle;
                var web = controller.CoreWebView2;
                Log($"runtime {environment.BrowserVersionString}");
                web.AddWebResourceRequestedFilter(https ? "https://*.idleviz.invalid/*" : "idleviz-app://*", CoreWebView2WebResourceContext.All);
                web.WebResourceRequested += (_, e) =>
                {
                    var uri = new Uri(e.Request.Uri);
                    var file = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/')));
                    var origin = e.Request.Headers.Contains("Origin") ? e.Request.Headers.GetHeader("Origin") : "(none)";
                    if (!File.Exists(file)) { Log($"404 {e.Request.Uri}"); e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
                    var type = Path.GetExtension(file) switch { ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8", ".ttf" => "font/ttf", _ => "application/octet-stream" };
                    var headers = $"Content-Type: {type}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: *";
                    if (type.StartsWith("text/html")) { headers += "\r\nContent-Security-Policy: " + (Path.GetFileName(file) == "plugin-host.html" ? pluginCsp : pageCsp); }
                    if (!uri.AbsolutePath.Contains("/vendor/")) { Log($"200 {uri.AbsolutePath} ({e.ResourceContext}, origin {origin})"); }
                    var bytes = File.ReadAllBytes(file);
                    if (https && Path.GetFileName(file) == "visualizer-state.js")
                    {
                        // The one page change this would need: the URL prefixes the page accepts for plugins and presets.
                        var text = System.Text.Encoding.UTF8.GetString(bytes).Replace("idleviz-app://presets/", "https://presets.idleviz.invalid/").Replace("idleviz-app://app/visuals/", "https://app.idleviz.invalid/visuals/");
                        bytes = System.Text.Encoding.UTF8.GetBytes(text);
                    }

                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", headers);
                };
                web.GetDevToolsProtocolEventReceiver("Log.entryAdded").DevToolsProtocolEventReceived += (_, e) => Log("PAGELOG " + e.ParameterObjectAsJson);
                web.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, e) => Log("CONSOLE " + (e.ParameterObjectAsJson.Length > 500 ? e.ParameterObjectAsJson[..500] : e.ParameterObjectAsJson));
                await web.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
                await web.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                web.NavigationCompleted += async (_, e) =>
                {
                    Log($"navigation success={e.IsSuccess} {e.WebErrorStatus}");
                    var entries = """{"entries":[{"id":"bundled:aurora","name":"Aurora","source":"bundled","kind":"plugin","url":"APPBASEvisuals/aurora.js","version":"1"}],"hung":[]}""".Replace("APPBASE", appBase);
                    await web.ExecuteScriptAsync($"window.setCustomPresets?.({entries})");
                    await web.ExecuteScriptAsync("""window.setPresetSettings?.({"mode":"single","single":"bundled:aurora"})""");
                };
                web.Navigate(appBase + "index.html");
                var timer = new System.Windows.Forms.Timer { Interval = 1000 };
                var n = 0;
                timer.Tick += async (_, _) =>
                {
                    n++;
                    if (n == seconds - 1) { Log("STATUS " + await web.ExecuteScriptAsync("JSON.stringify({s: window.idlevizStatus?.(), frames: document.querySelectorAll('iframe').length, origin: location.origin, secure: isSecureContext, sandbox: document.querySelector('iframe')?.getAttribute('sandbox')})")); }
                    if (n >= seconds) { Log("END"); Application.Exit(); }
                };
                timer.Start();
            }
            catch (Exception error)
            {
                Log("FAILED " + error);
                Application.Exit();
            }
        };
        Application.Run(form);
    }
}
