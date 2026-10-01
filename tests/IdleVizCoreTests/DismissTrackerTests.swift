import XCTest
@testable import IdleVizCore

final class DismissTrackerTests: XCTestCase {
    let control: UInt16 = 59
    let option: UInt16 = 58
    let keyA: UInt16 = 0
    let keyL: UInt16 = 37

    private func armed(holding keys: Set<UInt16> = []) -> DismissTracker {
        var tracker = DismissTracker(gracePeriod: 0.4)
        tracker.arm(heldKeys: keys, elapsed: 0.4)
        return tracker
    }

    // MARK: Grace period

    func testIgnoresEverythingBeforeArmed() {
        var tracker = DismissTracker(gracePeriod: 0.4)
        let events: [InputEvent] = [
            .mouseMoved(deltaX: 5, deltaY: 0), .mouseDown, .scroll, .gesture,
            .key(code: keyA, isDown: true, isRepeat: false),
        ]
        for event in events {
            XCTAssertFalse(tracker.shouldDismiss(on: event, elapsed: 0.2), "\(event)")
        }
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0, heldKeys: [], elapsed: 0.3))
    }

    // MARK: Events

    func testAnyInputClosesOnceArmed() {
        let events: [InputEvent] = [
            .mouseDown, .scroll, .gesture,
            .key(code: keyA, isDown: true, isRepeat: false),
            .key(code: control, isDown: true, isRepeat: false),
        ]
        for event in events {
            var tracker = armed()
            XCTAssertTrue(tracker.shouldDismiss(on: event, elapsed: 1), "\(event)")
        }
    }

    func testTinyMouseMoveCloses() {
        var tracker = armed()
        XCTAssertTrue(tracker.shouldDismiss(on: .mouseMoved(deltaX: 0, deltaY: -0.5), elapsed: 1))
    }

    func testZeroDeltaMouseMoveIsIgnored() {
        var tracker = armed()
        XCTAssertFalse(tracker.shouldDismiss(on: .mouseMoved(deltaX: 0, deltaY: 0), elapsed: 1))
    }

    func testReleasingAnUnheldKeyDoesNotClose() {
        var tracker = armed()
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyA, isDown: false, isRepeat: false), elapsed: 1))
    }

    // MARK: Keys held past the grace period

    func testReleasingHeldModifiersIsIgnored() {
        var tracker = armed(holding: [control, option])
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: control, isDown: false, isRepeat: false), elapsed: 0.9))
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: option, isDown: false, isRepeat: false), elapsed: 1.2))
        XCTAssertTrue(tracker.stuckKeys.isEmpty)
    }

    func testPressingAHeldKeyAgainCloses() {
        var tracker = armed(holding: [control])
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: control, isDown: false, isRepeat: false), elapsed: 0.9))
        XCTAssertTrue(tracker.shouldDismiss(on: .key(code: control, isDown: true, isRepeat: false), elapsed: 2))
    }

    func testKeyRepeatOfAHeldKeyIsIgnored() {
        var tracker = armed(holding: [keyA])
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyA, isDown: true, isRepeat: true), elapsed: 0.8))
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyA, isDown: true, isRepeat: true), elapsed: 0.9))
    }

    func testFreshPressOfAStuckKeyCloses() {
        // The release was missed (window not key), so a non-repeat press is new input.
        var tracker = armed(holding: [keyA])
        XCTAssertTrue(tracker.shouldDismiss(on: .key(code: keyA, isDown: true, isRepeat: false), elapsed: 2))
    }

    func testOtherInputStillClosesWhileAKeyIsHeld() {
        var tracker = armed(holding: [control])
        XCTAssertTrue(tracker.shouldDismiss(on: .key(code: keyA, isDown: true, isRepeat: false), elapsed: 1))
        var mouse = armed(holding: [control])
        XCTAssertTrue(mouse.shouldDismiss(on: .mouseDown, elapsed: 1))
    }

    // MARK: Like and skip keys

    func testAPassKeyNeverCloses() {
        var tracker = DismissTracker(gracePeriod: 0.4, passKeys: [keyL])
        tracker.arm(heldKeys: [], elapsed: 0.4)
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: true, isRepeat: false), elapsed: 1))
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: true, isRepeat: true), elapsed: 1.5))
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: false, isRepeat: false), elapsed: 1.6))
        // Pressed again, it still doesn't close.
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: true, isRepeat: false), elapsed: 2))
    }

    func testOtherKeysStillCloseNextToAPassKey() {
        var tracker = DismissTracker(gracePeriod: 0.4, passKeys: [keyL])
        tracker.arm(heldKeys: [], elapsed: 0.4)
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: true, isRepeat: false), elapsed: 1))
        XCTAssertTrue(tracker.shouldDismiss(on: .key(code: keyA, isDown: true, isRepeat: false), elapsed: 1.1))
    }

    func testBackupIgnoresAPassKeyTheEventMonitorHandled() {
        var tracker = DismissTracker(gracePeriod: 0.4, passKeys: [keyL])
        tracker.arm(heldKeys: [], elapsed: 0.4)
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: true, isRepeat: false), elapsed: 1.0))
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.03, heldKeys: [keyL], elapsed: 1.02))
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: false, isRepeat: false), elapsed: 1.1))
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.02, heldKeys: [], elapsed: 1.11))
        // Input after that is new.
        XCTAssertTrue(tracker.shouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [], elapsed: 2.0))
    }

    func testBackupClosesOnAPassKeyTheEventMonitorNeverSaw() {
        // The window isn't key, so the key went to another app and did nothing here.
        var tracker = DismissTracker(gracePeriod: 0.4, passKeys: [keyL])
        tracker.arm(heldKeys: [], elapsed: 0.4)
        XCTAssertTrue(tracker.shouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [keyL], elapsed: 3))
    }

    func testAPassKeyHeldSinceOpeningIsNotStuck() {
        var tracker = DismissTracker(gracePeriod: 0.4, passKeys: [keyL])
        tracker.arm(heldKeys: [keyL], elapsed: 0.4)
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: false, isRepeat: false), elapsed: 0.9))
        XCTAssertTrue(tracker.stuckKeys.isEmpty)
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: keyL, isDown: true, isRepeat: false), elapsed: 2))
    }

    // MARK: Media keys

    /// `data1` of a system-defined event: the key type in the high half, "key down" in the low half.
    private func data1(keyType: Int) -> Int { keyType << 16 | 0xA00 }

    func testMediaKeysAreRecognized() {
        // Volume up and down, brightness up and down, mute, play, next, previous, fast, rewind, backlight.
        for keyType in [0, 1, 2, 3, 7, 16, 17, 18, 19, 20, 21, 22, 23] {
            XCTAssertEqual(MediaKey.inputEvent(subtype: 8, data1: data1(keyType: keyType)), .mediaKey, "\(keyType)")
        }
        // The same key going up, and repeating.
        XCTAssertEqual(MediaKey.inputEvent(subtype: 8, data1: 0 << 16 | 0xB00), .mediaKey)
        XCTAssertEqual(MediaKey.inputEvent(subtype: 8, data1: 1 << 16 | 0xA01), .mediaKey)
    }

    func testOtherSystemKeysAreNotMediaKeys() {
        // Caps Lock, Help, power, Num Lock, contrast, the launch panel, Eject and video mirror.
        for keyType in [4, 5, 6, 10, 11, 12, 13, 14, 15, 24, 100] {
            XCTAssertNil(MediaKey.inputEvent(subtype: 8, data1: data1(keyType: keyType)), "\(keyType)")
        }
        // Other kinds of system-defined events.
        XCTAssertNil(MediaKey.inputEvent(subtype: 7, data1: data1(keyType: 0)))
        XCTAssertNil(MediaKey.inputEvent(subtype: 1, data1: 0))
    }

    func testAMediaKeyNeverCloses() {
        var tracker = armed()
        XCTAssertFalse(tracker.shouldDismiss(on: .mediaKey, elapsed: 1))
        XCTAssertFalse(tracker.shouldDismiss(on: .mediaKey, elapsed: 1.1))
        XCTAssertTrue(tracker.shouldDismiss(on: .key(code: keyA, isDown: true, isRepeat: false), elapsed: 1.2))
    }

    func testBackupIgnoresAMediaKeyTheEventMonitorHandled() {
        var tracker = armed()
        XCTAssertFalse(tracker.shouldDismiss(on: .mediaKey, elapsed: 1.0))
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.03, heldKeys: [], elapsed: 1.02))
        XCTAssertTrue(tracker.shouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [], elapsed: 2.0))
    }

    // MARK: Backup check

    func testBackupIgnoresTheTriggerInput() {
        var tracker = armed()
        // The hotkey was released 0.1 s after opening, 2 s ago.
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 2.0, heldKeys: [], elapsed: 2.1))
    }

    func testBackupClosesOnInputAfterGracePeriod() {
        var tracker = armed()
        XCTAssertTrue(tracker.shouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [], elapsed: 3))
    }

    func testBackupIgnoresReleaseOfHeldModifiers() {
        var tracker = armed(holding: [control, option])
        // Still held: the key repeat or held state keeps resetting the idle time.
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [control, option], elapsed: 0.5))
        // Control let go between checks.
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [option], elapsed: 0.6))
        // Option let go between checks.
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.03, heldKeys: [], elapsed: 1.7))
        // Nothing since.
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 1.03, heldKeys: [], elapsed: 2.7))
        // New input.
        XCTAssertTrue(tracker.shouldDismiss(secondsSinceLastInput: 0.02, heldKeys: [], elapsed: 3.0))
    }

    func testBackupIgnoresReleaseTheEventMonitorAlreadyHandled() {
        var tracker = armed(holding: [control])
        XCTAssertFalse(tracker.shouldDismiss(on: .key(code: control, isDown: false, isRepeat: false), elapsed: 1.0))
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [], elapsed: 1.05))
        XCTAssertTrue(tracker.shouldDismiss(secondsSinceLastInput: 0.01, heldKeys: [], elapsed: 2.0))
    }

    func testBackupPressingAHeldKeyAgainCloses() {
        var tracker = armed(holding: [control])
        XCTAssertFalse(tracker.shouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [], elapsed: 1.0))
        XCTAssertTrue(tracker.shouldDismiss(secondsSinceLastInput: 0.05, heldKeys: [control], elapsed: 2.0))
    }
}
