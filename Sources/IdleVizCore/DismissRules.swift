import Foundation

/// One user input, reduced to what the dismiss rules care about.
public enum InputEvent: Sendable, Equatable {
    case mouseMoved(deltaX: Double, deltaY: Double)
    case mouseDown
    case scroll
    case keyDown
    case flagsChanged
    case gesture
}

/// Decides when the visualizer closes. Pure logic, so the app's event monitors
/// and the ~100 ms backup check can both be tested without a screen.
public struct DismissRules: Sendable, Equatable {
    /// Input in this window after opening is ignored, so the trigger itself
    /// (the hotkey release, the click on "Open now") doesn't close it again.
    public var gracePeriod: TimeInterval

    public init(gracePeriod: TimeInterval = 0.4) {
        self.gracePeriod = gracePeriod
    }

    /// `elapsed` is the time since the window opened.
    public func shouldDismiss(on event: InputEvent, elapsed: TimeInterval) -> Bool {
        guard elapsed >= gracePeriod else { return false }
        switch event {
        case let .mouseMoved(deltaX, deltaY):
            // Trackpads can send zero-delta moves while nobody touches them.
            return deltaX != 0 || deltaY != 0
        case .mouseDown, .scroll, .keyDown, .flagsChanged, .gesture:
            return true
        }
    }

    /// Backup for events the monitors can't see (keys while the window isn't key).
    /// Closes when the system saw any input after the grace period ended.
    public func shouldDismiss(secondsSinceLastInput: TimeInterval, elapsed: TimeInterval) -> Bool {
        guard elapsed >= gracePeriod else { return false }
        return secondsSinceLastInput < elapsed - gracePeriod
    }
}
