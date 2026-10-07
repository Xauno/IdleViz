import XCTest
@testable import IdleVizCore

final class DisplayLayoutTests: XCTestCase {
    private let builtIn = Display(id: 1, frame: CGRect(x: 0, y: 0, width: 1512, height: 982), scale: 2)
    private let external = Display(id: 2, frame: CGRect(x: 1512, y: 0, width: 2560, height: 1440), scale: 1)

    private func tracker(_ displays: Display...) -> DisplayTracker {
        DisplayTracker(layout: DisplayLayout(displays: displays))
    }

    func testStaysOpenWhenOnlyTheDockOrMenuBarChanged() {
        var tracker = tracker(builtIn)
        // A new Dock tile changes the visible area, not the display.
        XCTAssertFalse(tracker.shouldClose(on: DisplayLayout(displays: [builtIn])))
        XCTAssertFalse(tracker.shouldClose(on: DisplayLayout(displays: [builtIn])))
    }

    func testClosesWhenADisplayIsAddedOrRemoved() {
        var tracker = tracker(builtIn)
        XCTAssertTrue(tracker.shouldClose(on: DisplayLayout(displays: [builtIn, external])))
        XCTAssertTrue(tracker.shouldClose(on: DisplayLayout(displays: [builtIn])))
    }

    func testClosesWhenTheResolutionChanges() {
        var tracker = tracker(builtIn)
        let scaled = Display(id: 1, frame: CGRect(x: 0, y: 0, width: 1800, height: 1169), scale: 2)
        XCTAssertTrue(tracker.shouldClose(on: DisplayLayout(displays: [scaled])))
        let lowDensity = Display(id: 1, frame: scaled.frame, scale: 1)
        XCTAssertTrue(tracker.shouldClose(on: DisplayLayout(displays: [lowDensity])))
    }

    func testClosesWhenAnotherDisplayBecomesTheMainOne() {
        var tracker = tracker(builtIn, external)
        XCTAssertTrue(tracker.shouldClose(on: DisplayLayout(displays: [external, builtIn])))
    }

    func testRemembersTheNewLayoutAfterAChange() {
        var tracker = tracker(builtIn)
        let both = DisplayLayout(displays: [builtIn, external])
        XCTAssertTrue(tracker.shouldClose(on: both))
        // The same layout again is no longer a change.
        XCTAssertFalse(tracker.shouldClose(on: both))
        XCTAssertEqual(tracker.layout, both)
    }
}
