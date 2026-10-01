import AppKit
import IdleVizCore
import os
import WebKit

/// The one web page: visualizer, dim layer and overlay. Swift talks to it only with
/// `evaluateJavaScript`; there is no `WKScriptMessageHandler`, so the page can't call into the app.
@MainActor
final class PageView: NSObject, WKNavigationDelegate, WKUIDelegate {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "page")
    /// Goes in the window. It holds the web view, which is replaced when the page hangs.
    let view = NSView()
    private var webView: WKWebView
    /// True once the page's scripts have run and its functions can be called.
    private var loaded = false
    /// Counts page loads, so a readiness check left over from an earlier load is ignored.
    private var loadID = 0
    /// Audio frames the page hasn't taken yet. More than a couple means it's busy, so newer frames are dropped.
    private var framesInFlight = 0
    private var statusTimer: Timer?
    private var watchdog = PageWatchdog()
    private var lastStatus: PageStatus?
    /// The latest `nowPlaying` call, replayed whenever the page (re)loads.
    private var nowPlayingScript = OverlayPayload.script(for: nil)
    /// The latest preset controls, replayed the same way.
    private var presetSettingsScript = PresetSettings().script
    /// The latest list of bundled plugins and custom presets, replayed the same way.
    private var customPresetsScript = CustomPresetPayload().script
    /// Called with the presets the page reports as failed, each time that list changes.
    var onFailures: (([PresetFailure]) -> Void)?
    /// Called with the preset that was on screen when the page stopped answering.
    var onHung: ((String) -> Void)?
    /// Called with the page's preset list each time the page has loaded.
    var onPresets: (([PresetInfo]) -> Void)?
    /// Called when a different preset comes on screen.
    var onPresetShown: ((String) -> Void)?

    override init() {
        webView = Self.makeWebView()
        super.init()
        install(webView)
    }

    private static func makeWebView() -> WKWebView {
        let config = WKWebViewConfiguration()
        let root = Bundle.main.resourceURL!.appending(path: "web")
        let handler = AppSchemeHandler(root: root, presetsRoot: PresetFolder.defaultURL)
        config.setURLSchemeHandler(handler, forURLScheme: AppScheme.scheme)
        config.mediaTypesRequiringUserActionForPlayback = []
        config.websiteDataStore = .nonPersistent()
        let webView = WKWebView(frame: .zero, configuration: config)
        webView.setValue(false, forKey: "drawsBackground")
        #if DEBUG
        webView.isInspectable = true
        #endif
        return webView
    }

    private func install(_ webView: WKWebView) {
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.frame = view.bounds
        webView.autoresizingMask = [.width, .height]
        view.addSubview(webView)
        webView.load(URLRequest(url: AppScheme.pageURL))
    }

    func show(_ payload: OverlayPayload?) {
        nowPlayingScript = OverlayPayload.script(for: payload)
        guard loaded else { return }
        webView.evaluateJavaScript(nowPlayingScript)
    }

    func send(customPresets: CustomPresetPayload) {
        customPresetsScript = customPresets.script
        guard loaded else { return }
        webView.evaluateJavaScript(customPresetsScript)
        fetchPresetList()
    }

    private func fetchPresetList() {
        webView.evaluateJavaScript("window.idlevizPresets?.()") { [weak self] reply, _ in
            MainActor.assumeIsolated { self?.onPresets?(PresetInfo.list(reply: reply)) }
        }
    }

    func send(presetSettings: PresetSettings) {
        presetSettingsScript = presetSettings.script
        guard loaded else { return }
        webView.evaluateJavaScript(presetSettingsScript)
    }

    /// Hands one packed audio frame to the page. Frames are dropped, not queued, while the page is busy or loading.
    func send(audioFrame: Data) {
        guard loaded, framesInFlight < 2 else { return }
        framesInFlight += 1
        webView.evaluateJavaScript(AudioFrame.script(for: audioFrame)) { [weak self] _, _ in
            MainActor.assumeIsolated { self?.framesInFlight -= 1 }
        }
    }

    /// While the window is open, asks the page once a second how it's doing. A page that stops
    /// answering (a preset or plugin stuck in a loop) is replaced.
    func startStatusChecks() {
        guard statusTimer == nil else { return }
        watchdog.start(at: ProcessInfo.processInfo.systemUptime)
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.checkStatus() }
        }
        RunLoop.main.add(timer, forMode: .common)
        statusTimer = timer
    }

    func stopStatusChecks() {
        statusTimer?.invalidate()
        statusTimer = nil
        watchdog.stop()
    }

    private func checkStatus() {
        if watchdog.shouldReload(at: ProcessInfo.processInfo.systemUptime) {
            let preset = lastStatus?.preset
            log.error("The page stopped answering; replacing it (last preset: \(preset ?? "none", privacy: .public))")
            replaceWebView()
            // Whatever was on screen is the likely cause. Keep it out, or the new page would hang on it too.
            if let preset { onHung?(preset) }
            return
        }
        guard loaded else { return }
        webView.evaluateJavaScript("window.idlevizStatus?.()") { [weak self] reply, _ in
            MainActor.assumeIsolated {
                guard let self, let status = PageStatus(reply: reply) else { return }
                self.watchdog.replied(at: ProcessInfo.processInfo.systemUptime)
                if status.preset != self.lastStatus?.preset {
                    self.log.notice("Preset: \(status.preset ?? "none", privacy: .public)")
                    if let preset = status.preset { self.onPresetShown?(preset) }
                }
                if let last = self.lastStatus {
                    let frames = status.frames - last.frames
                    let audio = status.audioFrames - last.audioFrames
                    self.log.debug("Page: \(frames, privacy: .public) frames, \(audio, privacy: .public) audio frames")
                }
                if status.failed != self.lastStatus?.failed {
                    let known = Set((self.lastStatus?.failed ?? []).map(\.id))
                    for failure in status.failed where !known.contains(failure.id) {
                        self.log.error("Failed to load \(failure.id, privacy: .public): \(failure.error, privacy: .public)")
                    }
                    self.onFailures?(status.failed)
                }
                self.lastStatus = status
            }
        }
    }

    private func resetPageState() {
        loaded = false
        loadID += 1
        framesInFlight = 0
        lastStatus = nil
    }

    /// A page stuck in a JavaScript loop can't be reloaded: the navigation never starts
    /// (checked in step 7b). A new web view gets a new web content process.
    private func replaceWebView() {
        resetPageState()
        let stuckProcess = Self.webContentProcess(of: webView)
        webView.navigationDelegate = nil
        webView.uiDelegate = nil
        webView.removeFromSuperview()
        webView = Self.makeWebView()
        install(webView)
        // WebKit leaves the stuck process spinning at full CPU after its web view is gone, so end it here.
        if let stuckProcess {
            kill(stuckProcess, SIGKILL)
            log.notice("Ended the stuck web content process \(stuckProcess, privacy: .public)")
        }
    }

    /// The process ID of the web view's content process. WebKit has no public way to ask, so this
    /// reads a private property and returns nil if a future WebKit no longer has it.
    private static func webContentProcess(of webView: WKWebView) -> pid_t? {
        let key = "_webProcessIdentifier"
        guard webView.responds(to: Selector(key)), let pid = webView.value(forKey: key) as? Int32, pid > 0 else { return nil }
        return pid
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard webView === self.webView else { return }
        loadID += 1
        waitUntilReady(load: loadID, attempt: 0)
    }

    /// WebKit can report the navigation as finished before the page's modules have run (seen in
    /// about one launch in five in step 7c), and a call made then is silently lost. So ask the
    /// page whether its functions exist yet, and only then send it the current state.
    private func waitUntilReady(load: Int, attempt: Int) {
        let check = "['nowPlaying', 'setPresetSettings', 'setCustomPresets'].every((name) => typeof window[name] === 'function')"
        webView.evaluateJavaScript(check) { [weak self] reply, _ in
            MainActor.assumeIsolated {
                guard let self, load == self.loadID, !self.loaded else { return }
                if reply as? Bool == true {
                    self.pageBecameReady()
                } else if attempt < 200 {
                    DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(50)) { [weak self] in
                        self?.waitUntilReady(load: load, attempt: attempt + 1)
                    }
                } else {
                    self.log.error("The page's scripts didn't start within 10 s")
                }
            }
        }
    }

    private func pageBecameReady() {
        loaded = true
        // The library goes first, so the settings can pick from all of it.
        webView.evaluateJavaScript(customPresetsScript)
        webView.evaluateJavaScript(presetSettingsScript)
        webView.evaluateJavaScript(nowPlayingScript)
        fetchPresetList()
    }

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction) async -> WKNavigationActionPolicy {
        // Only the app's own page may load in the top frame.
        action.request.url?.scheme == AppScheme.scheme ? .allow : .cancel
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        guard webView === self.webView else { return }
        log.error("Web content process ended; reloading")
        resetPageState()
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
