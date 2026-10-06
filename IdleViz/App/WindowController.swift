import AppKit
import IdleVizCore
import os

/// Borderless windows can't become key by default. Being key lets a local event
/// monitor see keys without Accessibility or Input Monitoring permission.
final class VisualizerWindow: NSWindow {
    /// False while input is ignored ("Close on input" off): the keyboard stays with the app that has it.
    var takesFocus = true

    override var canBecomeKey: Bool { takesFocus }
    override var canBecomeMain: Bool { takesFocus }

    static func make(frame: CGRect, content: NSView? = nil) -> VisualizerWindow {
        let window = VisualizerWindow(contentRect: frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        window.backgroundColor = .black
        window.isOpaque = true
        window.hasShadow = false
        window.isReleasedWhenClosed = false
        window.acceptsMouseMovedEvents = true
        if let content { window.contentView = content }
        window.setFrame(frame, display: false)
        return window
    }
}

/// Opens and closes the visualizer: fades, focus, the cursor and the dismiss watcher. There is one
/// main window with the main page. With more than one display mirrored there is a further window,
/// with a page of its own, for each other display; extended, the main window covers them all.
@MainActor
final class WindowController {
    private enum State { case closed, open, closing }

    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "window")
    // Kept between opens (hidden, not destroyed) so opening is instant.
    private var window: VisualizerWindow?
    // Created at launch and kept loaded, so the page is ready the first time the window opens.
    let page = PageView()
    /// A hidden window for each further display that is connected, whether or not it is covered.
    /// See `prepare` for why they are made before they are needed.
    private var otherWindows: [VisualizerWindow] = []
    /// The pages of the other displays that are covered. `mirrors[n]` sits in `otherWindows[n]`.
    private var mirrors: [PageView] = []
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

    /// Whether input closes the window that is open now: the debug switch and the "Close on input" setting.
    private(set) var closesOnInput = true

    /// False again as soon as it starts to fade out.
    var isOpen: Bool { state == .open }

    /// The windows in use, the main one first.
    private var openWindows: [VisualizerWindow] {
        (window.map { [$0] } ?? []) + otherWindows.prefix(mirrors.count)
    }

    /// Creates the hidden windows, and the pages of the other displays that are covered, so that an
    /// open finds them loaded. Call it at launch, while the app is still an accessory, and when the
    /// Displays settings or the displays change: macOS decides when a window is created whether it
    /// may join other apps' fullscreen Spaces, and a window created while the app is regular
    /// (settings open) never can. So every connected display gets its window here, not only the
    /// covered ones, and switching a display on in settings finds a window that already exists.
    /// It waits while the visualizer is open; the next open catches up.
    @discardableResult
    func prepare() -> DisplayPlan? {
        guard state == .closed else { return nil }
        let plan = Displays.plan
        if window == nil { window = VisualizerWindow.make(frame: plan.windows[0].frame, content: page.view) }

        let wanted = max(NSScreen.screens.count, plan.windows.count) - 1
        while otherWindows.count < wanted {
            otherWindows.append(VisualizerWindow.make(frame: plan.windows[0].frame))
        }
        while mirrors.count > plan.windows.count - 1 {
            let mirror = mirrors.removeLast()
            page.removeMirror(mirror)
            otherWindows[mirrors.count].contentView = NSView()
        }
        while mirrors.count < plan.windows.count - 1 {
            let mirror = PageView()
            // A preset that hangs a page hangs it on every display, so it is kept out the same way.
            mirror.onHung = { [weak self] preset in self?.page.onHung?(preset) }
            page.addMirror(mirror)
            otherWindows[mirrors.count].contentView = mirror.view
            mirrors.append(mirror)
            log.notice("Made a page for display \(self.mirrors.count + 1, privacy: .public)")
        }
        return plan
    }

    func open(source: TriggerSource) {
        guard state != .open else { return }
        // Triggered again while it fades out: finish that close first, so everything starts clean.
        if state == .closing { finishClosing() }
        guard let plan = prepare(), let window else { return }
        closesOnInput = dismissEnabled && plan.closeOnInput

        let frontmost = NSWorkspace.shared.frontmostApplication
        previousApp = frontmost?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : frontmost

        let pages = [page] + mirrors
        for (index, window) in openWindows.enumerated() {
            window.setFrame(plan.windows[index].frame, display: false)
            pages[index].send(layout: plan.windows[index])
            window.takesFocus = plan.closeOnInput
            window.ignoresMouseEvents = false
            window.alphaValue = 0
        }
        // With input ignored the keyboard stays with the app that has it.
        if plan.closeOnInput {
            NSApp.activate()
            window.makeKeyAndOrderFront(nil)
        }
        openWindows.forEach { $0.orderFrontRegardless() }
        state = .open
        fade(to: 1, seconds: VisualizerFade.openSeconds)
        onOpen?()

        if closesOnInput { watchForInput() }
        logOpened(source: source, window: window, takesFocus: plan.closeOnInput)
    }

    private func watchForInput() {
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

    private func logOpened(source: TriggerSource, window: VisualizerWindow, takesFocus: Bool) {
        let windows = openWindows.count
        guard takesFocus else {
            log.notice("Opened via \(source.rawValue, privacy: .public) on \(windows, privacy: .public) window(s): input is ignored")
            return
        }
        // Activation is cooperative and may be refused; give it a moment, then record the outcome.
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(250))
            guard let self, self.isOpen else { return }
            let activated = NSApp.isActive && window.isKeyWindow
            self.stats.record(activated: activated)
            let outcome = activated ? "active" : "activation refused"
            self.log.notice(
                """
                Opened via \(source.rawValue, privacy: .public) on \(windows, privacy: .public) window(s): \
                \(outcome, privacy: .public) (\(self.stats.summary, privacy: .public))
                """
            )
        }
    }

    /// Hands the Mac back at once (cursor, focus, clicks) and fades the windows out on top of it.
    func close(_ reason: CloseReason) {
        // A close that can't wait cuts a fade-out short.
        if state == .closing, reason.fadeSeconds == 0 { finishClosing() }
        guard state == .open else { return }
        state = .closing
        log.notice("Closing: \(reason.rawValue, privacy: .public)")
        dismissWatcher?.stop()
        dismissWatcher = nil
        openWindows.forEach { $0.ignoresMouseEvents = true }
        if cursorHidden {
            NSCursor.unhide()
            cursorHidden = false
        }
        // Only if focus is still here: with input ignored, someone may have switched apps while it was open.
        if NSApp.isActive || closesOnInput { previousApp?.activate() }
        previousApp = nil
        onClosing?()
        if reason.fadeSeconds > 0 {
            fade(to: 0, seconds: reason.fadeSeconds)
        } else {
            finishClosing()
        }
    }

    private func finishClosing() {
        guard state == .closing else { return }
        fadeID += 1
        window?.orderOut(nil)
        otherWindows.forEach { $0.orderOut(nil) }
        state = .closed
        onClose?()
    }

    private func fade(to alpha: CGFloat, seconds: TimeInterval) {
        fadeID += 1
        let id = fadeID
        let windows = openWindows
        NSAnimationContext.runAnimationGroup { context in
            context.duration = seconds
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            windows.forEach { $0.animator().alphaValue = alpha }
        } completionHandler: { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.fadeID == id else { return }
                self.finishClosing()
            }
        }
    }
}
