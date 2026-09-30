import AppKit
import IdleVizCore
import os
import WebKit

/// Borderless windows can't become key by default. Being key lets a local event
/// monitor see keys without Accessibility or Input Monitoring permission.
final class VisualizerWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    static func make(on screen: NSScreen) -> VisualizerWindow {
        let window = VisualizerWindow(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        window.backgroundColor = .black
        window.isOpaque = true
        window.hasShadow = false
        window.isReleasedWhenClosed = false
        window.acceptsMouseMovedEvents = true
        window.setFrame(screen.frame, display: false)
        return window
    }
}

@MainActor
final class WindowController {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "window")
    // Kept between opens (hidden, not destroyed) so opening is instant.
    private var window: VisualizerWindow?
    private var dismissWatcher: DismissWatcher?
    private var previousApp: NSRunningApplication?
    private var cursorHidden = false
    private var stats = ActivationStats()
    private var spike: AudioSpike?

    let dismissEnabled: Bool = {
        #if DEBUG
        // Launch argument `-IdleVizNoDismiss YES` keeps the window open for inspection.
        return !UserDefaults.standard.bool(forKey: "IdleVizNoDismiss")
        #else
        return true
        #endif
    }()

    var isOpen: Bool { window?.isVisible ?? false }

    func open(on screen: NSScreen, source: TriggerSource) {
        guard !isOpen else { return }
        let window = window ?? VisualizerWindow.make(on: screen)
        self.window = window
        window.setFrame(screen.frame, display: false)
        let spike = spike ?? AudioSpike(window: window)
        self.spike = spike
        spike.start()

        let frontmost = NSWorkspace.shared.frontmostApplication
        previousApp = frontmost?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : frontmost

        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        window.orderFrontRegardless()

        if dismissEnabled {
            NSCursor.hide()
            cursorHidden = true
            let watcher = DismissWatcher(tracker: DismissTracker()) { [weak self] in self?.close() }
            watcher.start()
            dismissWatcher = watcher
        }

        // Activation is cooperative and may be refused; give it a moment, then record the outcome.
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(250))
            guard let self, self.isOpen else { return }
            let activated = NSApp.isActive && window.isKeyWindow
            self.stats.record(activated: activated)
            let outcome = activated ? "active" : "activation refused"
            self.log.notice(
                "Opened via \(source.rawValue, privacy: .public): \(outcome, privacy: .public) (\(self.stats.summary, privacy: .public))"
            )
        }
    }

    func close() {
        guard isOpen else { return }
        dismissWatcher?.stop()
        dismissWatcher = nil
        window?.orderOut(nil)
        spike?.stop()
        if cursorHidden {
            NSCursor.unhide()
            cursorHidden = false
        }
        previousApp?.activate()
        previousApp = nil
    }
}

/// SPIKE (step 2, not merged): web view + Butterchurn fed by the Spotify tap at ~60 frames/s.
@MainActor
final class AudioSpike {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "spike")
    private let webView: WKWebView
    private let tap = SpotifyAudioTap()
    private var builder = FrameBuilder()
    private var timer: Timer?
    private var sent = 0
    private var inFlight = 0
    private var skippedBusy = 0
    private var buildTime = Duration.zero
    private var roundTrip = Duration.zero
    private var completed = 0
    private var zeroStreak = 0
    private var statsAt = ContinuousClock.now

    init(window: NSWindow) {
        let config = WKWebViewConfiguration()
        config.mediaTypesRequiringUserActionForPlayback = []
        // `-IdleVizSpikeMaxWidth 1920` caps the canvas width, to compare GPU cost.
        let maxWidth = UserDefaults.standard.integer(forKey: "IdleVizSpikeMaxWidth")
        config.userContentController.addUserScript(WKUserScript(
            source: "window.SPIKE_MAX_WIDTH = \(maxWidth); window.SPIKE_NO_CAP = \(UserDefaults.standard.bool(forKey: "IdleVizSpikeNoCap"));",
            injectionTime: .atDocumentStart, forMainFrameOnly: true
        ))
        webView = WKWebView(frame: window.contentLayoutRect, configuration: config)
        webView.autoresizingMask = [.width, .height]
        webView.underPageBackgroundColor = .black
        #if DEBUG
        webView.isInspectable = true
        #endif
        window.contentView = webView
        if let page = Bundle.main.url(forResource: "index", withExtension: "html") {
            webView.loadFileURL(page, allowingReadAccessTo: page.deletingLastPathComponent())
        } else {
            log.error("index.html missing from the bundle")
        }
    }

    func start() {
        tap.start()
        statsAt = .now
        let timer = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.sendFrame() }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        tap.stop()
    }

    private func sendFrame() {
        // Don't queue frames behind a busy page; drop instead.
        guard inFlight < 2 else { skippedBusy += 1; return }
        // `-IdleVizSpikeNoFrames YES` stops sending audio, to see whether evaluateJavaScript disturbs rendering.
        if UserDefaults.standard.bool(forKey: "IdleVizSpikeNoFrames") { logStatsIfDue(); return }
        let t0 = ContinuousClock.now
        let frame = builder.build(from: tap.ring, sampleRate: tap.sampleRate)
        // Guarded: frames sent before the page has loaded would otherwise throw.
        let js = "window.audioFrame?.('\(frame.base64EncodedString())')"
        buildTime += ContinuousClock.now - t0
        inFlight += 1
        sent += 1
        let sentAt = ContinuousClock.now
        webView.evaluateJavaScript(js) { [weak self] _, error in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.inFlight -= 1
                self.completed += 1
                self.roundTrip += ContinuousClock.now - sentAt
                if let error { self.log.error("audioFrame failed: \(error.localizedDescription, privacy: .public)") }
            }
        }
        logStatsIfDue()
    }

    private func logStatsIfDue() {
        let elapsed = ContinuousClock.now - statsAt
        guard elapsed >= .seconds(5) else { return }
        func ms(_ d: Duration) -> Double { Double(d.components.seconds) * 1e3 + Double(d.components.attoseconds) / 1e15 }
        let secs = ms(elapsed) / 1e3
        let (buffers, zero) = tap.ring.takeCounts()
        zeroStreak = (buffers > 0 && zero == buffers) ? zeroStreak + 1 : 0
        let buildUs = ms(buildTime) * 1e3 / Double(max(sent, 1))
        let rtMs = ms(roundTrip) / Double(max(completed, 1))
        let summary = String(
            format: "sent %.1f/s, skipped busy %d, build %.0f µs, evaluateJavaScript round trip %.2f ms, "
                + "tap buffers %d (%d all-zero), processes %@",
            Double(sent) / secs, skippedBusy, buildUs, rtMs, buffers, zero, "\(tap.tappedProcesses)"
        )
        log.notice("\(summary, privacy: .public)")
        webView.evaluateJavaScript("JSON.stringify(window.spikeStats())") { [weak self] result, _ in
            MainActor.assumeIsolated { self?.log.notice("page: \(String(describing: result ?? "nil"), privacy: .public)") }
        }
        if zeroStreak >= 2 { log.warning("Only exact-zero buffers for 10 s: permission probably denied (or Spotify paused)") }
        sent = 0; skippedBusy = 0; completed = 0; buildTime = .zero; roundTrip = .zero
        statsAt = .now
    }
}
