import Foundation

/// One user input, reduced to what the dismiss rules care about.
public enum InputEvent: Sendable, Equatable {
    case mouseMoved(deltaX: Double, deltaY: Double)
    case mouseDown
    case scroll
    case gesture
    /// A key or modifier going down or up. `isRepeat` marks key-repeat presses.
    case key(code: UInt16, isDown: Bool, isRepeat: Bool)
    /// One of the top-row keys that leave you watching: brightness, keyboard backlight, playback or volume.
    case mediaKey
}

/// The top-row keys that don't close the visualizer. They arrive as system-defined events, not as
/// key events, with the key in the event's data.
public enum MediaKey {
    /// The system-defined subtype that carries these keys (`NX_SUBTYPE_AUX_CONTROL_BUTTONS`).
    public static let subtype = 8

    /// The `NX_KEYTYPE_` values that keep the visualizer open: volume up and down (0, 1),
    /// brightness up and down (2, 3), mute (7), play, next and previous (16 to 18), fast forward and
    /// rewind, which is what Apple keyboards send for next and previous (19, 20), and the keyboard
    /// backlight (21 to 23). Caps Lock, Eject, the power key and the rest still close it.
    public static let keyTypes: Set<Int> = [0, 1, 2, 3, 7, 16, 17, 18, 19, 20, 21, 22, 23]

    /// The input event for a system-defined event, or nil if it isn't one of these keys.
    /// `data1` holds the key type in bits 16 to 31.
    public static func inputEvent(subtype: Int, data1: Int) -> InputEvent? {
        guard subtype == Self.subtype, keyTypes.contains((data1 >> 16) & 0xFFFF) else { return nil }
        return .mediaKey
    }
}

/// Decides when the visualizer closes. Pure logic, so the app's event monitors
/// and the ~100 ms backup check can both be tested without a screen.
///
/// Input during the grace period after opening is ignored, so the trigger itself
/// (the hotkey, the click on "Open now") doesn't close the window again. Keys still
/// held when the grace period ends are "stuck": letting go of them, or their key
/// repeat, is ignored too. Pressing one again closes the window.
///
/// The like and skip keys (`passKeys`) and the media keys never close the window when the
/// event monitors see them.
public struct DismissTracker: Sendable {
    public let gracePeriod: TimeInterval
    /// Key codes that do something else than close the window.
    public let passKeys: Set<UInt16>
    public private(set) var stuckKeys: Set<UInt16> = []
    private var armed = false
    /// Elapsed time up to which the backup check treats input as explained by stuck or pass keys.
    private var ignoreInputUntil: TimeInterval = 0

    public init(gracePeriod: TimeInterval = 0.4, passKeys: Set<UInt16> = []) {
        self.gracePeriod = gracePeriod
        self.passKeys = passKeys
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
        case .mediaKey:
            // Down, repeating or up: input the backup check shouldn't close on either.
            ignoreInputUntil = max(ignoreInputUntil, elapsed)
            return false
        case let .key(code, isDown, isRepeat):
            if passKeys.contains(code) {
                // Pressed, repeating or let go, it's input the backup check shouldn't close on either.
                stuckKeys.remove(code)
                ignoreInputUntil = max(ignoreInputUntil, elapsed)
                return false
            }
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
    /// `heldKeys` are the key codes down right now. A pass key the monitors didn't see
    /// went to another app, so it closes the window like any other key. So does a media key they didn't see.
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
