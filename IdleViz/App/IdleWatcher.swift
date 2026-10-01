import AppKit
import CoreGraphics
import IdleVizCore
import IOKit.pwr_mgt

/// Fires the idle trigger. Each check is scheduled for the earliest moment the timeout
/// could be reached (see `IdleScheduler`), so there is no fixed polling interval.
@MainActor
final class IdleWatcher {
    private var scheduler: IdleScheduler
    private let timeout: () -> TimeInterval?
    private let onIdle: () -> Void
    private var loop: Task<Void, Never>?
    private var observers: [(NotificationCenter, NSObjectProtocol)] = []

    /// - Parameter timeout: The timeout that applies right now. It depends on the settings and the power source.
    init(timeout: @escaping () -> TimeInterval?, onIdle: @escaping () -> Void) {
        self.timeout = timeout
        self.onIdle = onIdle
        scheduler = IdleScheduler(timeout: timeout())
    }

    func start() {
        let defaults = NotificationCenter.default
        observers.append((defaults, defaults.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.timeoutMayHaveChanged() }
        }))
        // Task.sleep doesn't count time asleep, so recheck after wake and unlock.
        let workspace = NSWorkspace.shared.notificationCenter
        observers.append((workspace, workspace.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.reschedule() }
        }))
        let distributed = DistributedNotificationCenter.default()
        observers.append((distributed, distributed.addObserver(
            forName: Notification.Name("com.apple.screenIsUnlocked"), object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.reschedule() }
        }))
        reschedule()
    }

    /// Call when the setting or the power source changed.
    func timeoutMayHaveChanged() {
        let timeout = timeout()
        guard timeout != scheduler.timeout else { return }
        scheduler.timeout = timeout
        reschedule()
    }

    private func reschedule() {
        loop?.cancel()
        loop = Task { [weak self] in
            while !Task.isCancelled {
                guard let self else { return }
                switch self.scheduler.check(now: ProcessInfo.processInfo.systemUptime, idle: Self.secondsSinceLastInput()) {
                case .off:
                    return
                case .fire:
                    self.onIdle()
                case .wait(let seconds):
                    // A little past the deadline, so the next check doesn't land just short of it.
                    try? await Task.sleep(for: .seconds(seconds + 0.1))
                }
            }
        }
    }

    /// After the keep-awake limit closed the visualizer, the Mac is still idle. Don't open again until there is input.
    func waitForInput() {
        scheduler.waitForInput(now: ProcessInfo.processInfo.systemUptime, idle: Self.secondsSinceLastInput())
        reschedule()
    }

    static func secondsSinceLastInput() -> TimeInterval {
        CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)
    }

    // MARK: Skip rules

    /// Checks the idle skip rules against the session right now.
    static func skip() -> IdleSkip? {
        IdleSkipRules.skip(currentConditions(), ignoredPIDs: ignoredPIDs())
    }

    private static func currentConditions() -> IdleConditions {
        let session = CGSessionCopyCurrentDictionary() as? [String: Any] ?? [:]
        return IdleConditions(
            screenLocked: session["CGSSessionScreenIsLocked"] as? Bool ?? false,
            onConsole: session[kCGSessionOnConsoleKey] as? Bool ?? true,
            assertions: powerAssertions()
        )
    }

    private static func powerAssertions() -> [PowerAssertion] {
        var byProcess: Unmanaged<CFDictionary>?
        guard IOPMCopyAssertionsByProcess(&byProcess) == kIOReturnSuccess,
              let dictionary = byProcess?.takeRetainedValue() as? [NSNumber: [[String: Any]]]
        else { return [] }
        return dictionary.flatMap { pid, assertions in
            assertions.map { assertion in
                PowerAssertion(
                    pid: pid.int32Value,
                    onBehalfOf: (assertion["AssertionOnBehalfOfPID"] as? NSNumber)?.int32Value,
                    type: assertion["AssertType"] as? String ?? "",
                    processName: assertion["Process Name"] as? String ?? "pid \(pid)"
                )
            }
        }
    }

    /// Spotify (with any helpers) and IdleViz itself don't count as keeping the display awake.
    private static func ignoredPIDs() -> Set<Int32> {
        var pids: Set<Int32> = [ProcessInfo.processInfo.processIdentifier]
        for app in NSWorkspace.shared.runningApplications
        where app.bundleIdentifier?.hasPrefix(SpotifyInfo.bundleID) == true {
            pids.insert(app.processIdentifier)
        }
        return pids
    }
}
