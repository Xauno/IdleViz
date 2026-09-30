import Foundation
import IdleVizCore
import WebKit

/// Serves the bundled `web/` folder at `idleviz-app://app/…`, with the CSP header on HTML.
/// `idleviz-app://presets/…` (the custom presets folder) arrives with step 7d.
final class AppSchemeHandler: NSObject, WKURLSchemeHandler {
    private let root: URL

    init(root: URL) {
        self.root = root
    }

    func webView(_ webView: WKWebView, start task: any WKURLSchemeTask) {
        guard let url = task.request.url,
              let file = AppScheme.file(for: url, in: root),
              let data = try? Data(contentsOf: file)
        else {
            let url = task.request.url ?? AppScheme.pageURL
            task.didReceive(HTTPURLResponse(url: url, statusCode: 404, httpVersion: "HTTP/1.1", headerFields: [:])!)
            task.didFinish()
            return
        }
        let type = AppScheme.mimeType(for: file)
        var headers = ["Content-Type": type, "Content-Length": String(data.count), "Cache-Control": "no-cache"]
        if type.hasPrefix("text/html") {
            headers["Content-Security-Policy"] = AppScheme.contentSecurityPolicy
        }
        task.didReceive(HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: headers)!)
        task.didReceive(data)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: any WKURLSchemeTask) {}
}
