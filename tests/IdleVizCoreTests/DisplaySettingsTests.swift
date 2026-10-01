import XCTest
@testable import IdleVizCore

final class DisplaySettingsTests: XCTestCase {
    private var defaults: UserDefaults!
    private let suite = "DisplaySettingsTests"

    override func setUp() {
        defaults = UserDefaults(suiteName: suite)
        defaults.removePersistentDomain(forName: suite)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
    }

    func testBrightnessDefaultsTo70Percent() {
        XCTAssertEqual(BrightnessSetting.value(in: defaults), 0.7)
        XCTAssertEqual(BrightnessSetting.label(0.7), "70%")
    }

    func testBrightnessStaysInsideTheSliderRange() {
        XCTAssertEqual(BrightnessSetting.normalized(0.2), 0.5)
        XCTAssertEqual(BrightnessSetting.normalized(4), 1)
        XCTAssertEqual(BrightnessSetting.normalized(.nan), 0.7)
        defaults.set(0.1, forKey: BrightnessSetting.key)
        XCTAssertEqual(BrightnessSetting.value(in: defaults), 0.5)
    }

    func testBrightnessIsWholePercent() {
        XCTAssertEqual(BrightnessSetting.normalized(0.8349), 0.83)
        XCTAssertEqual(BrightnessSetting.label(0.8349), "83%")
        XCTAssertEqual(BrightnessSetting.label(1), "100%")
    }

    func testBrightnessScript() {
        XCTAssertEqual(BrightnessSetting.script(for: 0.85), "window.setBrightness?.(0.85)")
        XCTAssertEqual(BrightnessSetting.script(for: 9), "window.setBrightness?.(1.0)")
    }

    func testABrightnessThatIsNotANumberIsTheDefault() {
        defaults.set("bright", forKey: BrightnessSetting.key)
        XCTAssertEqual(BrightnessSetting.value(in: defaults), 0.7)
    }

    func testTheOverlayIsOnUnlessSwitchedOff() {
        XCTAssertTrue(OverlaySetting.value(in: defaults))
        defaults.set(false, forKey: OverlaySetting.key)
        XCTAssertFalse(OverlaySetting.value(in: defaults))
    }

    func testOverlayScript() {
        XCTAssertEqual(OverlaySetting.script(for: true), "window.setOverlayEnabled?.(true)")
        XCTAssertEqual(OverlaySetting.script(for: false), "window.setOverlayEnabled?.(false)")
    }
}
