import Foundation
import IdleVizCore
import IOKit.pwr_mgt
import os

/// Keeps the display awake while the visualizer is open, up to a time limit counted from when
/// it opened. After the limit the Mac goes back to its normal sleep, screensaver and lock.
@MainActor
final class KeepAwake {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "keepawake")
    private let limit: () -> TimeInterval
    private let onLimit: () -> Void
    private var assertion = IOPMAssertionID(kIOPMNullAssertionID)
    private var openedAt: TimeInterval?
    private var scheduledLimit: TimeInterval?
    private var timer: Task<Void, Never>?
    private var observer: NSObjectProtocol?

    /// - Parameters:
    ///   - limit: The limit that applies right now, in seconds. It depends on the settings and the power source.
    ///   - onLimit: Called once when the limit is reached.
    init(limit: @escaping () -> TimeInterval, onLimit: @escaping () -> Void) {
        self.limit = limit
        self.onLimit = onLimit
    }

    func start() {
        guard openedAt == nil else { return }
        openedAt = ProcessInfo.processInfo.systemUptime
        let result = IOPMAssertionCreateWithName(
            kIOPMAssertionTypePreventUserIdleDisplaySleep as CFString,
            IOPMAssertionLevel(kIOPMAssertionLevelOn),
            "IdleViz visualizer" as CFString,
            &assertion
        )
        if result != kIOReturnSuccess {
            assertion = IOPMAssertionID(kIOPMNullAssertionID)
            log.error("Couldn't keep the display awake: \(result, privacy: .public)")
        }
        observer = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.limitMayHaveChanged() }
        }
        schedule()
    }

    func stop() {
        guard openedAt != nil else { return }
        openedAt = nil
        scheduledLimit = nil
        timer?.cancel()
        timer = nil
        if let observer { NotificationCenter.default.removeObserver(observer) }
        observer = nil
        if assertion != kIOPMNullAssertionID {
            IOPMAssertionRelease(assertion)
            assertion = IOPMAssertionID(kIOPMNullAssertionID)
        }
    }

    /// Call when the setting or the power source changed. The new limit still counts from when the
    /// window opened, so a shorter one may already be over.
    func limitMayHaveChanged() {
        guard openedAt != nil, limit() != scheduledLimit else { return }
        schedule()
    }

    private func schedule() {
        guard let openedAt else { return }
        let limit = limit()
        scheduledLimit = limit
        timer?.cancel()
        timer = Task { [weak self] in
            while !Task.isCancelled {
                let remaining = TimingSettings.remaining(limit: limit, openedAt: openedAt, now: ProcessInfo.processInfo.systemUptime)
                if remaining <= 0 {
                    self?.log.notice("Keep-awake limit of \(Int(limit / 60), privacy: .public) min reached")
                    self?.onLimit()
                    return
                }
                try? await Task.sleep(for: .seconds(remaining))
            }
        }
    }
}
