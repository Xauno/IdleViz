import AppKit
import IdleVizCore
import os

/// Borderless windows can't become key by default. Being key lets a local event
/// monitor see keys without Accessibility or Input Monitoring permission.
final class VisualizerWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    static func make(on screen: NSScreen, content: NSView) -> VisualizerWindow {
        let window = VisualizerWindow(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        window.backgroundColor = .black
        window.isOpaque = true
        window.hasShadow = false
        window.isReleasedWhenClosed = false
        window.acceptsMouseMovedEvents = true
        window.contentView = content
        window.setFrame(screen.frame, display: false)
        return window
    }
}

@MainActor
final class WindowController {
    private enum State { case closed, open, closing }

    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "window")
    // Kept between opens (hidden, not destroyed) so opening is instant.
    private var window: VisualizerWindow?
    // Created at launch and kept loaded, so the page is ready the first time the window opens.
    let page = PageView()
    private var state = State.closed
    /// Counts fades, so the end of one that was replaced by another does nothing.
    private var fadeID = 0
    private var dismissWatcher: DismissWatcher?
    private var previousApp: NSRunningApplication?
    private var cursorHidden = false
    private var stats = ActivationStats()
    var onOpen: (() -> Void)?
    /// Called when the fade-out starts. The page keeps running until `onClose`, so the visuals don't freeze mid-fade.
    var onClosing: (() -> Void)?
    var onClose: (() -> Void)?
    /// Called when the like or skip key is pressed while the window is open.
    var onAction: ((VisualizerAction) -> Void)?

    let dismissEnabled: Bool = {
        #if DEBUG
        // Launch argument `-IdleVizNoDismiss YES` keeps the window open for inspection.
        return !UserDefaults.standard.bool(forKey: "IdleVizNoDismiss")
        #else
        return true
        #endif
    }()

    /// False again as soon as it starts to fade out.
    var isOpen: Bool { state == .open }

    /// Creates the hidden window. Call this at launch, while the app is still an accessory:
    /// macOS decides when a window is created whether it may join other apps' fullscreen Spaces,
    /// and a window created while the app is regular (settings open) never can.
    func prepare(on screen: NSScreen) {
        guard window == nil else { return }
        window = VisualizerWindow.make(on: screen, content: page.view)
    }

    func open(on screen: NSScreen, source: TriggerSource) {
        guard state != .open else { return }
        // Triggered again while it fades out: finish that close first, so everything starts clean.
        if state == .closing { finishClosing() }
        let window = window ?? VisualizerWindow.make(on: screen, content: page.view)
        self.window = window
        window.setFrame(screen.frame, display: false)

        let frontmost = NSWorkspace.shared.frontmostApplication
        previousApp = frontmost?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : frontmost

        window.ignoresMouseEvents = false
        window.alphaValue = 0
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        window.orderFrontRegardless()
        state = .open
        fade(window, to: 1, seconds: VisualizerFade.openSeconds)
        onOpen?()

        if dismissEnabled {
            NSCursor.hide()
            cursorHidden = true
            // Read at each open: settings can't change while the visualizer is up, since any input closes it.
            let watcher = DismissWatcher(
                keys: VisualizerKeys(defaults: .standard),
                onAction: { [weak self] action in self?.onAction?(action) },
                onDismiss: { [weak self] in self?.close(.input) }
            )
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

    /// Hands the Mac back at once (cursor, focus, clicks) and fades the window out on top of it.
    func close(_ reason: CloseReason) {
        // A close that can't wait cuts a fade-out short.
        if state == .closing, reason.fadeSeconds == 0 { finishClosing() }
        guard state == .open, let window else { return }
        state = .closing
        log.notice("Closing: \(reason.rawValue, privacy: .public)")
        dismissWatcher?.stop()
        dismissWatcher = nil
        window.ignoresMouseEvents = true
        if cursorHidden {
            NSCursor.unhide()
            cursorHidden = false
        }
        previousApp?.activate()
        previousApp = nil
        onClosing?()
        if reason.fadeSeconds > 0 {
            fade(window, to: 0, seconds: reason.fadeSeconds)
        } else {
            finishClosing()
        }
    }

    private func finishClosing() {
        guard state == .closing else { return }
        fadeID += 1
        window?.orderOut(nil)
        state = .closed
        onClose?()
    }

    private func fade(_ window: NSWindow, to alpha: CGFloat, seconds: TimeInterval) {
        fadeID += 1
        let id = fadeID
        NSAnimationContext.runAnimationGroup { context in
            context.duration = seconds
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            window.animator().alphaValue = alpha
        } completionHandler: { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.fadeID == id else { return }
                self.finishClosing()
            }
        }
    }
}
