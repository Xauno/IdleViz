import Foundation
import IdleVizCore
import WebKit

/// Serves the bundled `web/` folder at `idleviz-app://app/…`, each HTML page with its own
/// Content-Security-Policy header, and the custom presets folder at `idleviz-app://presets/…`.
final class AppSchemeHandler: NSObject, WKURLSchemeHandler {
    private let root: URL
    private let presetsRoot: URL?

    /// - Parameter presetsRoot: The custom presets folder, or nil for a page that has no use for it.
    init(root: URL, presetsRoot: URL? = nil) {
        self.root = root
        self.presetsRoot = presetsRoot
    }

    func webView(_ webView: WKWebView, start task: any WKURLSchemeTask) {
        guard let url = task.request.url, let file = file(for: url), let data = try? Data(contentsOf: file) else {
            let url = task.request.url ?? AppScheme.pageURL
            task.didReceive(HTTPURLResponse(url: url, statusCode: 404, httpVersion: "HTTP/1.1", headerFields: [:])!)
            task.didFinish()
            return
        }
        let type = AppScheme.mimeType(for: file)
        var headers = [
            "Content-Type": type,
            "Content-Length": String(data.count),
            "Cache-Control": "no-cache",
            // A plugin's sandboxed frame has an opaque origin, so every script it loads (its runner
            // from the app, the plugin from either place) is a cross-origin request.
            "Access-Control-Allow-Origin": "*",
        ]
        if type.hasPrefix("text/html") {
            headers["Content-Security-Policy"] = AppScheme.contentSecurityPolicy(forPage: file)
        }
        task.didReceive(HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: headers)!)
        task.didReceive(data)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: any WKURLSchemeTask) {}

    private func file(for url: URL) -> URL? {
        if let file = AppScheme.file(for: url, in: root) { return file }
        return presetsRoot.flatMap { AppScheme.presetFile(for: url, in: $0) }
    }
}
