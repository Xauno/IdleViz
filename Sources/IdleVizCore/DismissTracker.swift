import Foundation

/// One user input, reduced to what the dismiss rules care about.
public enum InputEvent: Sendable, Equatable {
    case mouseMoved(deltaX: Double, deltaY: Double)
    case mouseDown
    case scroll
    case gesture
    /// A key or modifier going down or up. `isRepeat` marks key-repeat presses.
    case key(code: UInt16, isDown: Bool, isRepeat: Bool)
}

/// Decides when the visualizer closes. Pure logic, so the app's event monitors
/// and the ~100 ms backup check can both be tested without a screen.
///
/// Input during the grace period after opening is ignored, so the trigger itself
/// (the hotkey, the click on "Open now") doesn't close the window again. Keys still
/// held when the grace period ends are "stuck": letting go of them, or their key
/// repeat, is ignored too. Pressing one again closes the window.
public struct DismissTracker: Sendable {
    public let gracePeriod: TimeInterval
    public private(set) var stuckKeys: Set<UInt16> = []
    private var armed = false
    /// Elapsed time up to which the backup check treats input as explained by stuck keys.
    private var ignoreInputUntil: TimeInterval = 0

    public init(gracePeriod: TimeInterval = 0.4) {
        self.gracePeriod = gracePeriod
    }

    /// Call once when the grace period ends, with the key codes held at that moment.
    /// Until then, nothing closes the window.
    public mutating func arm(heldKeys: Set<UInt16>, elapsed: TimeInterval) {
        armed = true
        stuckKeys = heldKeys
        ignoreInputUntil = elapsed
    }

    /// `elapsed` is the time since the window opened.
    public mutating func shouldDismiss(on event: InputEvent, elapsed: TimeInterval) -> Bool {
        guard armed else { return false }
        switch event {
        case let .mouseMoved(deltaX, deltaY):
            // Trackpads can send zero-delta moves while nobody touches them.
            return deltaX != 0 || deltaY != 0
        case .mouseDown, .scroll, .gesture:
            return true
        case let .key(code, isDown, isRepeat):
            guard stuckKeys.contains(code) else { return isDown }
            if isDown && !isRepeat {
                // A fresh press means the release was missed; it counts as new input.
                stuckKeys.remove(code)
                return true
            }
            if !isDown { stuckKeys.remove(code) }
            ignoreInputUntil = max(ignoreInputUntil, elapsed)
            return false
        }
    }

    /// Backup for input the event monitors can't see (keys while the window isn't key).
    /// `heldKeys` are the key codes down right now.
    public mutating func shouldDismiss(
        secondsSinceLastInput: TimeInterval,
        heldKeys: Set<UInt16>,
        elapsed: TimeInterval
    ) -> Bool {
        guard armed else { return false }
        let released = stuckKeys.subtracting(heldKeys)
        stuckKeys.subtract(released)
        // A stuck key let go since the last check, or one still repeating, explains any input so far.
        if !released.isEmpty || !stuckKeys.isEmpty {
            ignoreInputUntil = max(ignoreInputUntil, elapsed)
        }
        let inputAt = elapsed - secondsSinceLastInput
        return inputAt > max(gracePeriod, ignoreInputUntil)
    }
}
