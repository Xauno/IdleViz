import AppKit
import IdleVizCore
import os
import WebKit

/// The one web page: visualizer, dim layer and overlay. Swift talks to it only with
/// `evaluateJavaScript`; there is no `WKScriptMessageHandler`, so the page can't call into the app.
@MainActor
final class PageView: NSObject, WKNavigationDelegate, WKUIDelegate {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "page")
    let webView: WKWebView
    private var loaded = false
    /// The latest `nowPlaying` call, replayed whenever the page (re)loads.
    private var nowPlayingScript = OverlayPayload.script(for: nil)

    override init() {
        let config = WKWebViewConfiguration()
        let root = Bundle.main.resourceURL!.appending(path: "web")
        config.setURLSchemeHandler(AppSchemeHandler(root: root), forURLScheme: AppScheme.scheme)
        config.mediaTypesRequiringUserActionForPlayback = []
        config.websiteDataStore = .nonPersistent()
        webView = WKWebView(frame: .zero, configuration: config)
        super.init()
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.setValue(false, forKey: "drawsBackground")
        #if DEBUG
        webView.isInspectable = true
        #endif
        webView.load(URLRequest(url: AppScheme.pageURL))
    }

    func show(_ payload: OverlayPayload?) {
        nowPlayingScript = OverlayPayload.script(for: payload)
        guard loaded else { return }
        webView.evaluateJavaScript(nowPlayingScript)
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        loaded = true
        webView.evaluateJavaScript(nowPlayingScript)
    }

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction) async -> WKNavigationActionPolicy {
        // Only the app's own page may load in the top frame.
        action.request.url?.scheme == AppScheme.scheme ? .allow : .cancel
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        log.error("Web content process ended; reloading")
        loaded = false
        webView.load(URLRequest(url: AppScheme.pageURL))
    }

    /// The visuals only ever see Spotify's audio, which reaches the page from Swift. No page
    /// or plugin code may get the microphone or any other capture device.
    func webView(
        _ webView: WKWebView,
        decideMediaCapturePermissionsFor origin: WKSecurityOrigin,
        initiatedBy frame: WKFrameInfo,
        type: WKMediaCaptureType
    ) async -> WKPermissionDecision {
        .deny
    }
}
