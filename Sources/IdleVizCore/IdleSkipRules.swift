import Foundation

/// One power assertion, as read from `IOPMCopyAssertionsByProcess`.
public struct PowerAssertion: Sendable, Equatable {
    public var pid: Int32
    /// Set when a daemon holds the assertion for another process (coreaudiod, for example).
    public var onBehalfOf: Int32?
    public var type: String
    public var processName: String

    public init(pid: Int32, onBehalfOf: Int32? = nil, type: String, processName: String) {
        self.pid = pid
        self.onBehalfOf = onBehalfOf
        self.type = type
        self.processName = processName
    }

    /// Assertion types that keep the display awake: fullscreen video, calls, presentations.
    public static let displayTypes: Set<String> = ["PreventUserIdleDisplaySleep", "NoDisplaySleepAssertion"]
}

/// What the idle trigger sees of the session when it fires.
public struct IdleConditions: Sendable, Equatable {
    public var screenLocked: Bool
    public var onConsole: Bool
    public var assertions: [PowerAssertion]

    public init(screenLocked: Bool, onConsole: Bool, assertions: [PowerAssertion]) {
        self.screenLocked = screenLocked
        self.onConsole = onConsole
        self.assertions = assertions
    }
}

/// Why the idle trigger didn't open. Manual triggers skip these checks.
public enum IdleSkip: Sendable, Equatable {
    case screenLocked
    /// Another user is on the console (fast user switching).
    case notOnConsole
    /// Another process keeps the display awake. Carries its name for the log.
    case displayKeptAwake(holder: String)
}

public enum IdleSkipRules {
    /// - Parameter ignoredPIDs: Spotify's and IdleViz's own processes, whose assertions don't count.
    public static func skip(_ conditions: IdleConditions, ignoredPIDs: Set<Int32>) -> IdleSkip? {
        if conditions.screenLocked { return .screenLocked }
        if !conditions.onConsole { return .notOnConsole }
        let holder = conditions.assertions.first { assertion in
            PowerAssertion.displayTypes.contains(assertion.type)
                && !ignoredPIDs.contains(assertion.pid)
                && !(assertion.onBehalfOf.map(ignoredPIDs.contains) ?? false)
        }
        if let holder { return .displayKeptAwake(holder: holder.processName) }
        return nil
    }
}
