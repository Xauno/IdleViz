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

    /// The file inside `root` that an `idleviz-app://app/…` URL names, or nil if the URL
    /// is for another host or would escape `root` (`..`, symlinks).
    public static func file(for url: URL, in root: URL) -> URL? {
        guard url.scheme?.lowercased() == scheme, url.host()?.lowercased() == "app" else { return nil }
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
