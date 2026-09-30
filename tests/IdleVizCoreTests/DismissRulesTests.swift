import XCTest
@testable import IdleVizCore

final class DismissRulesTests: XCTestCase {
    let rules = DismissRules(gracePeriod: 0.4)

    func testIgnoresEverythingDuringGracePeriod() {
        let events: [InputEvent] = [.mouseMoved(deltaX: 5, deltaY: 0), .mouseDown, .scroll, .keyDown, .flagsChanged, .gesture]
        for event in events {
            XCTAssertFalse(rules.shouldDismiss(on: event, elapsed: 0.1), "\(event)")
            XCTAssertFalse(rules.shouldDismiss(on: event, elapsed: 0.399), "\(event)")
        }
    }

    func testAnyInputClosesAfterGracePeriod() {
        let events: [InputEvent] = [.mouseDown, .scroll, .keyDown, .flagsChanged, .gesture]
        for event in events {
            XCTAssertTrue(rules.shouldDismiss(on: event, elapsed: 0.4), "\(event)")
            XCTAssertTrue(rules.shouldDismiss(on: event, elapsed: 60), "\(event)")
        }
    }

    func testTinyMouseMoveCloses() {
        XCTAssertTrue(rules.shouldDismiss(on: .mouseMoved(deltaX: 1, deltaY: 0), elapsed: 1))
        XCTAssertTrue(rules.shouldDismiss(on: .mouseMoved(deltaX: 0, deltaY: -0.5), elapsed: 1))
    }

    func testZeroDeltaMouseMoveIsIgnored() {
        XCTAssertFalse(rules.shouldDismiss(on: .mouseMoved(deltaX: 0, deltaY: 0), elapsed: 1))
    }

    func testBackupIgnoresTheTriggerInput() {
        // The hotkey was released 0.1 s after opening, 2 s ago.
        XCTAssertFalse(rules.shouldDismiss(secondsSinceLastInput: 2.0, elapsed: 2.1))
    }

    func testBackupClosesOnInputAfterGracePeriod() {
        // Opened 3 s ago, a key was pressed 0.05 s ago.
        XCTAssertTrue(rules.shouldDismiss(secondsSinceLastInput: 0.05, elapsed: 3))
    }

    func testBackupWaitsForGracePeriod() {
        XCTAssertFalse(rules.shouldDismiss(secondsSinceLastInput: 0, elapsed: 0.2))
    }

    func testBackupAtExactGraceBoundary() {
        // Input exactly when the grace period ended counts as the trigger, not new input.
        XCTAssertFalse(rules.shouldDismiss(secondsSinceLastInput: 1.6, elapsed: 2.0))
        XCTAssertTrue(rules.shouldDismiss(secondsSinceLastInput: 1.59, elapsed: 2.0))
    }
}
