import XCTest
@testable import IdleVizCore

final class MultiDisplayTests: XCTestCase {
    // A wide main display with a smaller one to its left, their top edges level, and a third to the
    // right that sits 200 points lower. AppKit counts y upwards from the main display's bottom edge.
    private let wide = Display(id: 1, frame: CGRect(x: 0, y: 0, width: 3440, height: 1440), scale: 1, uuid: "WIDE", model: "Odyssey G85SB")
    private let side = Display(
        id: 2, frame: CGRect(x: -1920, y: 360, width: 1920, height: 1080), scale: 1, uuid: "SIDE", model: "DELL U2415"
    )
    private let third = Display(
        id: 3, frame: CGRect(x: 3440, y: -200, width: 2560, height: 1440), scale: 2, uuid: "THIRD", model: "DELL U2415"
    )
    private var two: [Display] { [wide, side] }
    private var three: [Display] { [wide, side, third] }

    private var defaults: UserDefaults!
    private let suiteName = "MultiDisplayTests"

    override func setUp() {
        defaults = UserDefaults(suiteName: suiteName)
        defaults.removePersistentDomain(forName: suiteName)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suiteName)
    }

    private func plan(_ settings: MultiDisplaySettings, _ displays: [Display], canSpan: Bool = true) -> DisplayPlan {
        DisplayPlan(settings: settings, displays: displays, canSpan: canSpan)
    }

    func testNothingStoredCoversTheMainDisplayAndClosesOnInput() {
        let settings = MultiDisplaySettings(defaults: defaults)
        XCTAssertEqual(settings, MultiDisplaySettings())
        XCTAssertNil(settings.otherDisplays)

        let plan = plan(settings, two)
        XCTAssertEqual(plan.windows, [
            PlannedWindow(
                frame: wide.frame,
                regions: [DisplayRegion(left: 0, top: 0, width: 3440, height: 1440, overlay: true)],
                renderWidthCap: DisplayPlan.renderWidthCap
            ),
        ])
        XCTAssertTrue(plan.closeOnInput)
    }

    func testTheSettingsAreReadFromTheDefaults() {
        defaults.set("SIDE", forKey: MultiDisplaySettings.mainDisplayKey)
        defaults.set(true, forKey: MultiDisplaySettings.enabledKey)
        defaults.set(["WIDE"], forKey: MultiDisplaySettings.otherDisplaysKey)
        defaults.set(false, forKey: MultiDisplaySettings.closeOnInputKey)
        defaults.set("extend", forKey: MultiDisplaySettings.placementKey)
        defaults.set(MultiDisplaySettings.overlayOnAll, forKey: MultiDisplaySettings.overlayDisplayKey)

        XCTAssertEqual(MultiDisplaySettings(defaults: defaults), MultiDisplaySettings(
            mainDisplay: "SIDE",
            enabled: true,
            otherDisplays: ["WIDE"],
            closeOnInput: false,
            placement: .extend,
            overlayDisplay: MultiDisplaySettings.overlayOnAll
        ))
    }

    func testAnUnknownPlacementIsMirror() {
        defaults.set("diagonal", forKey: MultiDisplaySettings.placementKey)
        XCTAssertEqual(MultiDisplaySettings(defaults: defaults).placement, .mirror)
    }

    func testTheMainDisplayCanBeAnotherThanTheOneMacOSCallsMain() {
        let plan = plan(MultiDisplaySettings(mainDisplay: "SIDE"), two)
        XCTAssertEqual(plan.windows.map(\.frame), [side.frame])
        XCTAssertEqual(plan.windows[0].regions.map(\.overlay), [true])
    }

    func testAMainDisplayThatIsNotConnectedFallsBackToTheFirst() {
        XCTAssertEqual(plan(MultiDisplaySettings(mainDisplay: "GONE"), two).windows.map(\.frame), [wide.frame])
    }

    func testMirrorGivesEachDisplayItsOwnWindowTheMainOneFirst() {
        let plan = plan(MultiDisplaySettings(mainDisplay: "SIDE", enabled: true), three)
        XCTAssertEqual(plan.windows.map(\.frame), [side.frame, wide.frame, third.frame])
        for window in plan.windows {
            let size = window.frame.size
            let onMain = window.frame == side.frame
            let region = DisplayRegion(left: 0, top: 0, width: Int(size.width), height: Int(size.height), overlay: onMain)
            XCTAssertEqual(window.regions, [region])
            XCTAssertEqual(window.renderWidthCap, DisplayPlan.renderWidthCap)
        }
    }

    func testOnlyTheChosenOtherDisplaysAreCovered() {
        var settings = MultiDisplaySettings(enabled: true, otherDisplays: ["THIRD", "GONE"])
        XCTAssertEqual(plan(settings, three).windows.map(\.frame), [wide.frame, third.frame])

        // None chosen: the switch is on, but only the main display is covered.
        settings.otherDisplays = []
        XCTAssertEqual(plan(settings, three).windows.count, 1)
    }

    func testTheSwitchOffCoversOnlyTheMainDisplayWhateverElseIsStored() {
        let settings = MultiDisplaySettings(enabled: false, closeOnInput: false, placement: .extend, overlayDisplay: "SIDE")
        let plan = plan(settings, two)
        XCTAssertEqual(plan.windows.map(\.frame), [wide.frame])
        XCTAssertEqual(plan.windows[0].regions.map(\.overlay), [true])
        // Close on input doesn't depend on the switch: it works with one display too.
        XCTAssertFalse(plan.closeOnInput)
    }

    func testInputCanBeIgnoredOnOneDisplayOrSeveral() {
        XCTAssertFalse(plan(MultiDisplaySettings(closeOnInput: false), two).closeOnInput)
        XCTAssertTrue(plan(MultiDisplaySettings(), two).closeOnInput)
        XCTAssertFalse(plan(MultiDisplaySettings(enabled: true, closeOnInput: false), two).closeOnInput)
        XCTAssertTrue(plan(MultiDisplaySettings(enabled: true), two).closeOnInput)
    }

    func testExtendIsOneWindowOverTheEnclosingRectangle() {
        let plan = plan(MultiDisplaySettings(enabled: true, placement: .extend), two)
        XCTAssertEqual(plan.windows.count, 1)
        let window = plan.windows[0]
        XCTAssertEqual(window.frame, CGRect(x: -1920, y: 0, width: 5360, height: 1440))
        XCTAssertEqual(window.regions, [
            DisplayRegion(left: 1920, top: 0, width: 3440, height: 1440, overlay: true),
            DisplayRegion(left: 0, top: 0, width: 1920, height: 1080, overlay: false),
        ])
        // 2560 px for the 3440-point display's share of the 5360-point span.
        XCTAssertEqual(window.renderWidthCap, 3989)
    }

    func testExtendWithASmallMainDisplayStopsAtThePagesLimit() {
        let settings = MultiDisplaySettings(mainDisplay: "SIDE", enabled: true, placement: .extend)
        let plan = plan(settings, three)
        XCTAssertEqual(plan.windows.count, 1)
        let window = plan.windows[0]
        XCTAssertEqual(window.frame, CGRect(x: -1920, y: -200, width: 7920, height: 1640))
        XCTAssertEqual(window.renderWidthCap, DisplayPlan.maxRenderWidthCap)
        // The third display sits 200 points below the top of the span.
        XCTAssertEqual(window.regions[2], DisplayRegion(left: 5360, top: 200, width: 2560, height: 1440, overlay: false))
    }

    func testExtendWithOneDisplayIsThatDisplay() {
        let plan = plan(MultiDisplaySettings(enabled: true, placement: .extend), [wide])
        XCTAssertEqual(plan.windows.map(\.frame), [wide.frame])
        XCTAssertEqual(plan.windows[0].renderWidthCap, DisplayPlan.renderWidthCap)
    }

    func testExtendFallsBackToAWindowPerDisplayWhenAWindowCannotSpan() {
        let settings = MultiDisplaySettings(enabled: true, placement: .extend, overlayDisplay: MultiDisplaySettings.overlayOnAll)
        let plan = plan(settings, two, canSpan: false)
        XCTAssertEqual(plan.windows.map(\.frame), [wide.frame, side.frame])
        XCTAssertEqual(plan.windows.map(\.renderWidthCap), [DisplayPlan.renderWidthCap, DisplayPlan.renderWidthCap])
        XCTAssertEqual(plan.windows.flatMap(\.regions).map(\.overlay), [true, true])
    }

    func testTheOverlayGoesOnTheChosenDisplayOrOnAll() {
        var settings = MultiDisplaySettings(enabled: true, overlayDisplay: "SIDE")
        XCTAssertEqual(plan(settings, two).windows.map { $0.regions[0].overlay }, [false, true])

        settings.overlayDisplay = MultiDisplaySettings.overlayOnAll
        XCTAssertEqual(plan(settings, two).windows.map { $0.regions[0].overlay }, [true, true])

        settings.placement = .extend
        XCTAssertEqual(plan(settings, two).windows[0].regions.map(\.overlay), [true, true])
    }

    func testAnOverlayDisplayThatIsNotCoveredFallsBackToTheMainOne() {
        let settings = MultiDisplaySettings(enabled: true, otherDisplays: ["THIRD"], overlayDisplay: "SIDE")
        XCTAssertEqual(plan(settings, three).windows.map { $0.regions[0].overlay }, [true, false])
    }

    func testNoDisplayAtAllStillPlansAWindow() {
        let plan = plan(MultiDisplaySettings(enabled: true), [])
        XCTAssertEqual(plan.windows.map(\.frame.size), [CGSize(width: 1920, height: 1080)])
    }

    func testTheScriptTellsThePageItsCapAndItsRegions() {
        let plan = plan(MultiDisplaySettings(enabled: true, placement: .extend), two)
        XCTAssertEqual(
            plan.windows[0].script,
            "window.setRenderWidthCap?.(3989);window.setOverlayRegions?.({\"height\":1440,\"regions\":["
                + "{\"height\":1440,\"overlay\":true,\"width\":3440,\"x\":1920,\"y\":0},"
                + "{\"height\":1080,\"overlay\":false,\"width\":1920,\"x\":0,\"y\":0}],\"width\":5360})"
        )
    }

    func testDisplaysAreLabelledByNameAndNumberedWhenTheNameRepeats() {
        XCTAssertEqual(DisplayLabel.labels(for: three), ["Odyssey G85SB", "DELL U2415", "DELL U2415 (2)"])
        let unnamed = Display(id: 4, frame: CGRect(x: 0, y: 0, width: 2560, height: 1440), scale: 1, uuid: "X", model: " ")
        XCTAssertEqual(DisplayLabel.labels(for: [wide, unnamed]), ["Odyssey G85SB", "Display 2 (2560 × 1440)"])
    }

    func testAFollowingPageIsHeldOnTheMainPagesPreset() {
        var settings = PresetSettings()
        settings.blendSeconds = 5
        let script = settings.followScript(preset: "bundled:Tunnel \"2\"")
        XCTAssertTrue(script.contains("\"mode\":\"single\""))
        XCTAssertTrue(script.contains("\"single\":\"bundled:Tunnel \\\"2\\\"\""))
        XCTAssertTrue(script.contains("\"blendSeconds\":5"))
        // The main page's own settings are untouched.
        XCTAssertTrue(settings.script.contains("\"mode\":\"shuffle\""))
    }
}
