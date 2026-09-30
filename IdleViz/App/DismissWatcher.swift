import AppKit
import IdleVizCore

/// Closes the visualizer on any input. A local monitor sees everything while the
/// window is key; a global mouse monitor and a ~100 ms idle-time check cover the
/// case where macOS refused to activate the app. None of these need permissions.
@MainActor
final class DismissWatcher {
    private static let localMask: NSEvent.EventTypeMask = [
        .mouseMoved, .leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel,
        .keyDown, .flagsChanged, .gesture, .magnify, .swipe,
    ]
    private static let globalMask: NSEvent.EventTypeMask = [
        .mouseMoved, .leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel,
        .gesture, .magnify, .swipe,
    ]

    private let rules: DismissRules
    private let onDismiss: () -> Void
    private let openedAt = ProcessInfo.processInfo.systemUptime
    private var localMonitor: Any?
    private var globalMonitor: Any?
    private var backupCheck: Task<Void, Never>?

    init(rules: DismissRules, onDismiss: @escaping () -> Void) {
        self.rules = rules
        self.onDismiss = onDismiss
    }

    private var elapsed: TimeInterval { ProcessInfo.processInfo.systemUptime - openedAt }

    func start() {
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: Self.localMask) { [weak self] event in
            MainActor.assumeIsolated { self?.handle(event) }
            // Swallow all input while open so nothing reaches views or beeps.
            return nil
        }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: Self.globalMask) { [weak self] event in
            MainActor.assumeIsolated { self?.handle(event) }
        }
        backupCheck = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(100))
                guard let self, !Task.isCancelled else { return }
                let idle = CGEventSource.secondsSinceLastEventType(
                    .combinedSessionState, eventType: CGEventType(rawValue: ~0)!
                )
                if self.rules.shouldDismiss(secondsSinceLastInput: idle, elapsed: self.elapsed) {
                    self.fire()
                    return
                }
            }
        }
    }

    func stop() {
        if let localMonitor { NSEvent.removeMonitor(localMonitor) }
        if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }
        localMonitor = nil
        globalMonitor = nil
        backupCheck?.cancel()
        backupCheck = nil
    }

    private func handle(_ event: NSEvent) {
        guard let input = Self.inputEvent(from: event) else { return }
        if rules.shouldDismiss(on: input, elapsed: elapsed) { fire() }
    }

    private func fire() {
        stop()
        onDismiss()
    }

    private static func inputEvent(from event: NSEvent) -> InputEvent? {
        switch event.type {
        case .mouseMoved: .mouseMoved(deltaX: event.deltaX, deltaY: event.deltaY)
        case .leftMouseDown, .rightMouseDown, .otherMouseDown: .mouseDown
        case .scrollWheel: .scroll
        case .keyDown: .keyDown
        case .flagsChanged: .flagsChanged
        case .gesture, .magnify, .swipe: .gesture
        default: nil
        }
    }
}
