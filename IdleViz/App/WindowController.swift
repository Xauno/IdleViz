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
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "window")
    // Kept between opens (hidden, not destroyed) so opening is instant.
    private var window: VisualizerWindow?
    // Created at launch and kept loaded, so the page is ready the first time the window opens.
    let page = PageView()
    private var dismissWatcher: DismissWatcher?
    private var previousApp: NSRunningApplication?
    private var cursorHidden = false
    private var stats = ActivationStats()
    var onOpen: (() -> Void)?
    var onClose: (() -> Void)?

    let dismissEnabled: Bool = {
        #if DEBUG
        // Launch argument `-IdleVizNoDismiss YES` keeps the window open for inspection.
        return !UserDefaults.standard.bool(forKey: "IdleVizNoDismiss")
        #else
        return true
        #endif
    }()

    var isOpen: Bool { window?.isVisible ?? false }

    /// Creates the hidden window. Call this at launch, while the app is still an accessory:
    /// macOS decides when a window is created whether it may join other apps' fullscreen Spaces,
    /// and a window created while the app is regular (settings open) never can.
    func prepare(on screen: NSScreen) {
        guard window == nil else { return }
        window = VisualizerWindow.make(on: screen, content: page.view)
    }

    func open(on screen: NSScreen, source: TriggerSource) {
        guard !isOpen else { return }
        let window = window ?? VisualizerWindow.make(on: screen, content: page.view)
        self.window = window
        window.setFrame(screen.frame, display: false)

        let frontmost = NSWorkspace.shared.frontmostApplication
        previousApp = frontmost?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : frontmost

        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        window.orderFrontRegardless()
        onOpen?()

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
        if cursorHidden {
            NSCursor.unhide()
            cursorHidden = false
        }
        previousApp?.activate()
        previousApp = nil
        onClose?()
    }
}
