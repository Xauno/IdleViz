using IdleViz.Core;
using Microsoft.Web.WebView2.Core;

namespace IdleViz.App;

/// <summary>Answers the app's own addresses for a WebView2: the visualizer page and the hidden converter page.</summary>
internal static class WebServer
{
    /// <summary>
    /// Answers the app's two addresses from the web folder and the presets folder. A handler that
    /// throws makes the request fail with no other sign (W2 spike), so nothing may escape it.
    /// </summary>
    /// <param name="environment">The environment the response is made in. Nothing is answered without one.</param>
    /// <param name="e">The request.</param>
    /// <param name="root">The web folder.</param>
    /// <param name="presetsRoot">The custom presets folder, or null where presets aren't served.</param>
    public static void Respond(CoreWebView2Environment? environment, CoreWebView2WebResourceRequestedEventArgs e, string root, string? presetsRoot)
    {
        if (environment is null)
        {
            return;
        }

        var url = e.Request.Uri;
        try
        {
            var file = AppAddresses.FileFor(url, root) ?? (presetsRoot is null ? null : AppAddresses.PresetFile(url, presetsRoot));
            if (file is null)
            {
                // The browser asks for a favicon on every load; only other misses are worth a line.
                if (!url.EndsWith("/favicon.ico", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info("page", $"404 {url}");
                }

                e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
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

            e.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes).AsRandomAccessStream(), 200, "OK", headers);
        }
        catch (Exception error)
        {
            Log.Info("page", $"Serving {url} failed: {error.Message}");
            try
            {
                e.Response = environment.CreateWebResourceResponse(null, 500, "Internal Server Error", string.Empty);
            }
            catch (Exception)
            {
                // Nothing more to do; the request fails.
            }
        }
    }
}
