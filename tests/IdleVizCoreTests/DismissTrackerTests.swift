import XCTest
@testable import IdleVizCore

final class DismissTrackerTests: XCTestCase {
    let control: UInt16 = 59
    let option: UInt16 = 58
    let keyA: UInt16 = 0

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
