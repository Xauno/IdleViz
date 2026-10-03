using System.Diagnostics;
using System.Text.Json;
using IdleViz.Core;
using Microsoft.Web.WebView2.Core;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>
/// Converts original Milkdrop <c>.milk</c> presets to Butterchurn's JSON in a hidden web page of
/// its own (<c>converter.html</c>), so untrusted files are parsed away from the visualizer page
/// and a slow conversion never stalls the visuals. The page lives in its own WebView2 profile, so
/// it gets its own renderer process, and exists only while there is work. Ported from
/// <c>MilkConverter.swift</c>.
/// </summary>
internal sealed class MilkConverter
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(50);

    private readonly Func<Task<CoreWebView2Environment?>> _environment;
    private readonly HWND _parent;
    private readonly string _root;
    private CoreWebView2Controller? _controller;

    // Kept with the controller: its event handlers live on this wrapper (see PageView).
    private CoreWebView2? _web;
    private int _call;

    /// <param name="environment">The page's WebView2 environment, once it exists.</param>
    /// <param name="parent">A window to host the hidden page. It is never shown there.</param>
    public MilkConverter(Func<Task<CoreWebView2Environment?>> environment, HWND parent)
    {
        _environment = environment;
        _parent = parent;
        _root = Path.Combine(AppContext.BaseDirectory, "web");
    }

    /// <summary>Converts one file's text. Fails with a message fit for the "Failed to load" list.</summary>
    public async Task<(string? Json, string? Error)> Convert(string source)
    {
        if (MilkConversion.Problem(source) is { } problem)
        {
            return (null, problem);
        }

        if (await Prepare() is not { } web)
        {
            return (null, "The converter didn't start");
        }

        var call = ++_call;
        // A JSON string is a JavaScript string literal; the serializer escapes everything that could end it.
        var start = $$"""
            (() => {
              window.idlevizResult = null;
              window.convertMilk({{JsonSerializer.Serialize(source)}}).then(
                (json) => { window.idlevizResult = { call: {{call}}, json }; },
                (error) => { window.idlevizResult = { call: {{call}}, error: String((error && error.message) || error) }; });
              return true;
            })()
            """;
        var clock = Stopwatch.StartNew();
        try
        {
            if (await WithTimeout(web.ExecuteScriptAsync(start).AsTask(), s_timeout) is null)
            {
                return TookTooLong();
            }

            while (clock.Elapsed < s_timeout)
            {
                await Task.Delay(s_poll);
                var reply = await WithTimeout(web.ExecuteScriptAsync($"window.idlevizResult?.call === {call} ? window.idlevizResult : null").AsTask(), s_timeout - clock.Elapsed);
                if (reply is null)
                {
                    break;
                }

                if (reply == "null")
                {
                    continue;
                }

                return Read(reply);
            }
        }
        catch (Exception error)
        {
            Close();
            return (null, PageStatus.Clip(error.Message));
        }

        return TookTooLong();
    }

    /// <summary>Lets go of the page. Called when there is nothing left to convert, and when a conversion is stuck.</summary>
    public void Close()
    {
        var controller = _controller;
        _controller = null;
        _web = null;
        try
        {
            controller?.Close();
        }
        catch (Exception error)
        {
            Log.Info("presets", $"Closing the converter page failed: {error.Message}");
        }
    }

    // The reply is the object the page stored: { call, json } or { call, error }. Untrusted, like the file.
    private static (string? Json, string? Error) Read(string reply)
    {
        try
        {
            using var document = JsonDocument.Parse(reply, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return (null, PageStatus.Clip(error.GetString()!));
            }

            return MilkConversion.CheckResult(
                root.ValueKind == JsonValueKind.Object && root.TryGetProperty("json", out var json) && json.ValueKind == JsonValueKind.String ? json.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, "The converter returned nothing");
        }
    }

    // A stuck conversion can't be interrupted, so the page goes; the next file gets a new one.
    private (string? Json, string? Error) TookTooLong()
    {
        Close();
        return (null, "The conversion took too long");
    }

    private static async Task<string?> WithTimeout(Task<string> task, TimeSpan limit)
    {
        if (limit <= TimeSpan.Zero)
        {
            return null;
        }

        return await Task.WhenAny(task, Task.Delay(limit)) == task ? await task : null;
    }

    private async Task<CoreWebView2?> Prepare()
    {
        if (_controller is not null && _web is { } existing)
        {
            return existing;
        }

        if (await _environment() is not { } environment)
        {
            return null;
        }

        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            // A profile of its own means a renderer of its own: a file that hangs the converter can't hang the visuals.
            options.ProfileName = "converter";
            var reference = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)(nint)_parent);
            var controller = await environment.CreateCoreWebView2ControllerAsync(reference, options);
            controller.IsVisible = false;
            var web = controller.CoreWebView2;
            var settings = web.Settings;
            settings.IsWebMessageEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            web.AddWebResourceRequestedFilter($"{AppAddresses.AppOrigin}/*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, e) => WebServer.Respond(environment, e, _root, presetsRoot: null);
            web.NavigationStarting += (_, e) => e.Cancel = !AppAddresses.IsAppPage(e.Uri);
            web.FrameNavigationStarting += (_, e) => e.Cancel = true;
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.DownloadStarting += (_, e) => e.Cancel = true;
            web.ProcessFailed += (_, _) => Close();
            _controller = controller;
            _web = web;
            web.Navigate(AppAddresses.ConverterUrl);

            // The page's function exists once its scripts have run; ask until it does.
            for (var attempt = 0; attempt < 200 && _controller == controller; attempt++)
            {
                if (await web.ExecuteScriptAsync("typeof window.convertMilk === 'function'") == "true")
                {
                    return web;
                }

                await Task.Delay(s_poll);
            }

            Log.Info("presets", "The preset converter page didn't start within 10 s");
        }
        catch (Exception error)
        {
            Log.Info("presets", $"The preset converter page didn't start: {error.Message}");
        }

        Close();
        return null;
    }
}
