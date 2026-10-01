import Foundation

/// Decides when the idle trigger fires, from the time since the last input.
///
/// Instead of polling on a fixed interval, each check says when the next one is due: at the
/// earliest moment the timeout could be reached. Each idle period gets one attempt. If that
/// attempt is blocked (no track, locked screen, a video playing), it waits for new input.
public struct IdleScheduler: Sendable, Equatable {
    public enum Step: Sendable, Equatable {
        /// Try to open now.
        case fire
        /// Check again after this many seconds.
        case wait(TimeInterval)
        /// The idle trigger is turned off.
        case off
    }

    /// Seconds of no input before opening, or nil when the idle trigger is off.
    public var timeout: TimeInterval?
    /// When the last input happened (uptime), for the idle period that was already attempted.
    private var attemptedPeriod: TimeInterval?
    /// Two readings of the same idle period differ by timer jitter; new input moves it by more.
    private static let tolerance: TimeInterval = 0.5

    public init(timeout: TimeInterval?) {
        self.timeout = timeout
    }

    /// - Parameters:
    ///   - now: system uptime in seconds.
    ///   - idle: seconds since the last input.
    public mutating func check(now: TimeInterval, idle: TimeInterval) -> Step {
        guard let timeout, timeout > 0 else { return .off }
        let lastInput = now - idle
        if let attemptedPeriod, abs(lastInput - attemptedPeriod) < Self.tolerance {
            // Still the same idle period. Any new input restarts the full timeout, so this is the earliest it could fire.
            return .wait(timeout)
        }
        guard idle >= timeout else { return .wait(timeout - idle) }
        attemptedPeriod = lastInput
        return .fire
    }

    /// Call when the keep-awake limit closes the visualizer. The Mac is still idle, so without
    /// this the trigger would open it again at once. The current idle period counts as used.
    public mutating func waitForInput(now: TimeInterval, idle: TimeInterval) {
        attemptedPeriod = now - idle
    }
}

/// The "Start after idle" setting, stored in minutes. 0 means off.
public enum IdleTimeoutSetting {
    public static let key = "idleTimeout"
    public static let defaultMinutes = 5
    public static let choices = [5, 10, 15, 30]

    public static func timeout(minutes: Int) -> TimeInterval? {
        minutes > 0 ? TimeInterval(minutes * 60) : nil
    }
}
