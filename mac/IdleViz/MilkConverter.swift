import Foundation
import IdleVizCore
import os
import WebKit

/// Converts original Milkdrop `.milk` presets to Butterchurn's JSON in a hidden web page of its
/// own (`converter.html`), so untrusted files are parsed away from the visualizer page and a
/// slow conversion never stalls the visuals. The page exists only while there is work.
@MainActor
final class MilkConverter: NSObject, WKNavigationDelegate {
    private static let timeout = Duration.seconds(10)

    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "presets")
    private var webView: WKWebView?
    private var ready = false

    /// Converts one file's text. Fails with a message fit for the "Failed to load" list.
    func convert(_ source: String) async -> Result<Data, MilkConversion.ConversionError> {
        if let problem = MilkConversion.problem(withSource: source) { return .failure(.init(problem)) }
        guard await prepare(), let webView else { return .failure(.init("The converter didn't start")) }
        let call = Task { @MainActor () -> Result<Data, MilkConversion.ConversionError> in
            do {
                let reply = try await webView.callAsyncJavaScript(
                    "return await window.convertMilk(source)", arguments: ["source": source], contentWorld: .page
                )
                return MilkConversion.checkResult(reply)
            } catch {
                return .failure(.init(PageStatus.clip(Self.message(for: error))))
            }
        }
        let watchdog = Task { @MainActor [weak self] in
            try? await Task.sleep(for: Self.timeout)
            guard !Task.isCancelled else { return }
            // A stuck conversion can't be interrupted, so the page goes; the next file gets a new one.
            self?.close()
        }
        let result = await call.value
        watchdog.cancel()
        // If the watchdog closed the page, WebKit fails the call with its own wording.
        return self.webView == nil ? .failure(.init("The conversion took too long")) : result
    }

    /// Lets go of the page. Call when there is nothing left to convert.
    func close() {
        webView?.navigationDelegate = nil
        webView = nil
        ready = false
    }

    private func prepare() async -> Bool {
        if ready { return true }
        if webView == nil {
            let config = WKWebViewConfiguration()
            let root = Bundle.main.resourceURL!.appending(path: "web")
            config.setURLSchemeHandler(AppSchemeHandler(root: root), forURLScheme: AppScheme.scheme)
            config.websiteDataStore = .nonPersistent()
            let webView = WKWebView(frame: .zero, configuration: config)
            webView.navigationDelegate = self
            webView.load(URLRequest(url: AppScheme.converterURL))
            self.webView = webView
        }
        // The page's function exists once its scripts have run; ask until it does.
        for _ in 0..<200 {
            guard let webView else { return false }
            if (try? await webView.evaluateJavaScript("typeof window.convertMilk === 'function'")) as? Bool == true {
                ready = true
                return true
            }
            try? await Task.sleep(for: .milliseconds(50))
        }
        log.error("The preset converter page didn't start within 10 s")
        close()
        return false
    }

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction) async -> WKNavigationActionPolicy {
        action.request.url?.scheme == AppScheme.scheme ? .allow : .cancel
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        close()
    }

    /// The JavaScript error's own message when there is one.
    private static func message(for error: any Error) -> String {
        let info = (error as NSError).userInfo
        return info["WKJavaScriptExceptionMessage"] as? String ?? error.localizedDescription
    }
}
