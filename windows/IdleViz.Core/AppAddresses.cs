namespace IdleViz.Core;

/// <summary>
/// The addresses the page is served from, and what each one may load. The Mac serves the page from
/// its own <c>idleviz-app://</c> scheme. WebView2 refuses every request from the plugin frame's
/// opaque origin to a custom scheme (W2 spike), so Windows answers two <c>https:</c> addresses
/// under the reserved <c>.invalid</c> name instead. The app answers them itself before anything
/// reaches the network, and the name never resolves anywhere.
/// </summary>
public static class AppAddresses
{
    /// <summary>The bundled <c>web</c> folder.</summary>
    public const string AppHost = "app.idleviz.invalid";

    /// <summary>The custom presets folder: presets (<c>.json</c>) and plugins (<c>.js</c>) only.</summary>
    public const string PresetsHost = "presets.idleviz.invalid";

    public const string AppOrigin = "https://" + AppHost;
    public const string PresetsOrigin = "https://" + PresetsHost;
    public const string PageUrl = AppOrigin + "/index.html";
    public const string ConverterUrl = AppOrigin + "/converter.html";

    /// <summary>
    /// Sent with the host page. <c>'unsafe-eval'</c> is for Butterchurn, which compiles preset
    /// equations with <c>new Function</c>. The Mac's policy with its scheme swapped for these hosts.
    /// </summary>
    public static readonly string ContentSecurityPolicy = string.Join(
        "; ",
        "default-src 'none'",
        "script-src 'self' 'unsafe-eval'",
        "style-src 'self'",
        "font-src 'self'",
        "img-src 'self' data: blob:",
        "connect-src 'self' " + PresetsOrigin,
        "frame-src 'self'");

    /// <summary>
    /// Sent with <c>plugin-host.html</c>, the sandboxed frame one plugin runs in: scripts from the
    /// app's two addresses only (the Mac allows its whole scheme, which is the same two places),
    /// no network, no storage.
    /// </summary>
    public static readonly string PluginFrameContentSecurityPolicy = string.Join(
        "; ",
        "default-src 'none'",
        $"script-src {AppOrigin} {PresetsOrigin}",
        "img-src data: blob:");

    /// <summary>
    /// Sent with <c>converter.html</c>, the hidden page that converts <c>.milk</c> files. The
    /// converter is WebAssembly and compiles equations, which is what <c>'unsafe-eval'</c> allows.
    /// </summary>
    public const string ConverterContentSecurityPolicy = "default-src 'none'; script-src 'self' 'unsafe-eval'";

    private static readonly Dictionary<string, string> s_mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".ttf"] = "font/ttf",
        [".woff2"] = "font/woff2",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".svg"] = "image/svg+xml",
        [".txt"] = "text/plain; charset=utf-8",
    };

    /// <summary>The policy for an HTML file of the app, by file name.</summary>
    public static string ContentSecurityPolicyFor(string fileName) => Path.GetFileName(fileName).ToLowerInvariant() switch
    {
        "plugin-host.html" => PluginFrameContentSecurityPolicy,
        "converter.html" => ConverterContentSecurityPolicy,
        _ => ContentSecurityPolicy,
    };

    public static string MimeType(string file) =>
        s_mimeTypes.TryGetValue(Path.GetExtension(file), out var type) ? type : "application/octet-stream";

    /// <summary>Whether a page may be shown at this address: the app's own pages only.</summary>
    public static bool IsAppPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.Equals(uri.Host, AppHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The file inside <paramref name="root"/> that an address on <paramref name="host"/> names, or
    /// null if the address is for another place, would leave <paramref name="root"/> (<c>..</c>,
    /// encoded or not), passes through a link, or doesn't name an existing file.
    /// </summary>
    public static string? FileFor(string? url, string root, string host = AppHost)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        // Uri has already removed plain dot segments; this looks at what is left once decoded.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        if (segments.Count == 0 || !segments.All(IsPlainName))
        {
            return null;
        }

        var baseFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var file = Path.GetFullPath(Path.Combine([baseFolder, .. segments]));
        if (!file.StartsWith(baseFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(file) && !PassesThroughLink(baseFolder, segments) ? file : null;
    }

    /// <summary>
    /// The file in the custom presets folder that a <c>https://presets.idleviz.invalid/…</c>
    /// address names. Only presets (<c>.json</c>) and plugins (<c>.js</c>) are served.
    /// </summary>
    public static string? PresetFile(string? url, string root)
    {
        var file = FileFor(url, root, PresetsHost);
        var extension = Path.GetExtension(file);
        return extension is not null
            && (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || extension.Equals(".js", StringComparison.OrdinalIgnoreCase))
            ? file
            : null;
    }

    /// <summary>The address the page uses for a file in the presets folder, from its path relative to the folder.</summary>
    public static string PresetUrl(string relativePath)
    {
        var parts = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return $"{PresetsOrigin}/{string.Join('/', parts)}";
    }

    // One folder or file name, with nothing Windows would read as more than that: no dot segments,
    // no separators, no drive or stream (":"), no control characters, and no trailing dot or space,
    // which Windows drops (so "plugin.js." would be served as "plugin.js").
    private static bool IsPlainName(string segment) =>
        segment is not ("." or "..")
        && !segment.EndsWith('.')
        && !segment.EndsWith(' ')
        && segment.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
        && !segment.Any(char.IsControl);

    // A link (symbolic link or junction) anywhere below the root could point out of it. Links in
    // the root's own path are fine: the root is what the user chose.
    private static bool PassesThroughLink(string baseFolder, List<string> segments)
    {
        var path = baseFolder;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }
}
