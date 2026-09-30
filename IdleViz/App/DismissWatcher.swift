import AppKit
import IdleVizCore

/// Closes the visualizer on any input. A local monitor sees everything while the
/// window is key; a global mouse monitor and a ~100 ms idle-time check cover the
/// case where macOS refused to activate the app. None of these need permissions.
/// The rules themselves, including keys still held after the grace period, live in
/// `DismissTracker`.
@MainActor
final class DismissWatcher {
    private static let localMask: NSEvent.EventTypeMask = [
        .mouseMoved, .leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel,
        .keyDown, .keyUp, .flagsChanged, .gesture, .magnify, .swipe,
    ]
    private static let globalMask: NSEvent.EventTypeMask = [
        .mouseMoved, .leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel,
        .gesture, .magnify, .swipe,
    ]

    private var tracker: DismissTracker
    private let onDismiss: () -> Void
    private let openedAt = ProcessInfo.processInfo.systemUptime
    private var localMonitor: Any?
    private var globalMonitor: Any?
    private var backupCheck: Task<Void, Never>?

    init(tracker: DismissTracker, onDismiss: @escaping () -> Void) {
        self.tracker = tracker
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
        let grace = tracker.gracePeriod
        backupCheck = Task { @MainActor [weak self] in
            try? await Task.sleep(for: .seconds(grace))
            guard !Task.isCancelled else { return }
            // Keys still down now are the trigger's; their release is ignored.
            self?.tracker.arm(heldKeys: Self.heldKeys(), elapsed: self?.elapsed ?? grace)
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(100))
                guard let self, !Task.isCancelled else { return }
                let idle = CGEventSource.secondsSinceLastEventType(
                    .combinedSessionState, eventType: CGEventType(rawValue: ~0)!
                )
                let dismiss = self.tracker.shouldDismiss(
                    secondsSinceLastInput: idle, heldKeys: Self.heldKeys(), elapsed: self.elapsed
                )
                if dismiss {
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
        if tracker.shouldDismiss(on: input, elapsed: elapsed) { fire() }
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
        case .keyDown: .key(code: event.keyCode, isDown: true, isRepeat: event.isARepeat)
        case .keyUp: .key(code: event.keyCode, isDown: false, isRepeat: false)
        // A modifier changed; whether it went down or up is read from the key state.
        case .flagsChanged: .key(code: event.keyCode, isDown: isKeyDown(event.keyCode), isRepeat: false)
        case .gesture, .magnify, .swipe: .gesture
        default: nil
        }
    }

    private static func isKeyDown(_ code: UInt16) -> Bool {
        CGEventSource.keyState(.combinedSessionState, key: CGKeyCode(code))
    }

    /// Virtual key codes (0–127, modifiers included) that are down right now.
    private static func heldKeys() -> Set<UInt16> {
        Set((0..<128).filter { isKeyDown($0) })
    }
}
