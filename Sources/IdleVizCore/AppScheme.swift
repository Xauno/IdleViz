import Foundation

/// The internal `idleviz-app://` scheme the page is served from. It gives the page one origin,
/// working ES modules and a place to send the Content-Security-Policy header.
/// `idleviz://` stays the external "open" scheme and is never loaded in the page.
public enum AppScheme {
    public static let scheme = "idleviz-app"
    public static let pageURL = URL(string: "idleviz-app://app/index.html")!

    /// Sent with the host page. `'unsafe-eval'` is for Butterchurn, which compiles preset equations with `new Function`.
    public static let contentSecurityPolicy = [
        "default-src 'none'",
        "script-src 'self' 'unsafe-eval'",
        "style-src 'self'",
        "font-src 'self'",
        "img-src 'self' data: blob:",
        "connect-src 'self' idleviz-app://presets",
        "frame-src 'self'",
    ].joined(separator: "; ")

    /// Sent with `plugin-host.html`, the sandboxed frame one plugin runs in: scripts from the app's
    /// own scheme only, no network, no storage.
    public static let pluginFrameContentSecurityPolicy = [
        "default-src 'none'",
        "script-src idleviz-app:",
        "img-src data: blob:",
    ].joined(separator: "; ")

    /// Sent with `converter.html`, the hidden page that converts `.milk` files. The converter is
    /// WebAssembly and compiles equations, which is what `'unsafe-eval'` allows; it gets nothing else.
    public static let converterContentSecurityPolicy = "default-src 'none'; script-src 'self' 'unsafe-eval'"

    public static let converterURL = URL(string: "idleviz-app://app/converter.html")!
    public static let appHost = "app"
    public static let presetsHost = "presets"

    /// The policy for an HTML file of the app, by file name.
    public static func contentSecurityPolicy(forPage file: URL) -> String {
        switch file.lastPathComponent {
        case "plugin-host.html": pluginFrameContentSecurityPolicy
        case "converter.html": converterContentSecurityPolicy
        default: contentSecurityPolicy
        }
    }

    /// The file in the custom presets folder that an `idleviz-app://presets/…` URL names. Only
    /// presets (`.json`) and plugins (`.js`) are served, and nothing outside the folder.
    public static func presetFile(for url: URL, in root: URL) -> URL? {
        guard let file = file(for: url, in: root, host: presetsHost) else { return nil }
        return ["json", "js"].contains(file.pathExtension.lowercased()) ? file : nil
    }

    /// The URL the page uses for a file in the presets folder, from its path relative to the folder.
    public static func presetURL(relativePath: String) -> String {
        var allowed = CharacterSet.urlPathAllowed
        allowed.remove(charactersIn: "%?#;")
        let path = relativePath.split(separator: "/").map { part in
            String(part).addingPercentEncoding(withAllowedCharacters: allowed) ?? ""
        }.joined(separator: "/")
        return "\(scheme)://\(presetsHost)/\(path)"
    }

    /// The file inside `root` that an `idleviz-app://<host>/…` URL names, or nil if the URL
    /// is for another host or would escape `root` (`..`, symlinks).
    public static func file(for url: URL, in root: URL, host: String = appHost) -> URL? {
        guard url.scheme?.lowercased() == scheme, url.host()?.lowercased() == host else { return nil }
        let relative = url.path(percentEncoded: false).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard !relative.isEmpty else { return nil }
        let base = root.standardizedFileURL.resolvingSymlinksInPath()
        let file = base.appending(path: relative).standardizedFileURL.resolvingSymlinksInPath()
        guard file.path.hasPrefix(base.path + "/") else { return nil }
        return file
    }

    private static let mimeTypes = [
        "html": "text/html; charset=utf-8",
        "js": "text/javascript; charset=utf-8",
        "mjs": "text/javascript; charset=utf-8",
        "css": "text/css; charset=utf-8",
        "json": "application/json",
        "ttf": "font/ttf",
        "woff2": "font/woff2",
        "png": "image/png",
        "jpg": "image/jpeg",
        "jpeg": "image/jpeg",
        "svg": "image/svg+xml",
        "txt": "text/plain; charset=utf-8",
    ]

    public static func mimeType(for file: URL) -> String {
        mimeTypes[file.pathExtension.lowercased()] ?? "application/octet-stream"
    }
}
