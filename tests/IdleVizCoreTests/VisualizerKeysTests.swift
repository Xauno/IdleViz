import XCTest
@testable import IdleVizCore

final class VisualizerKeysTests: XCTestCase {
    private var defaults: UserDefaults!
    private let suite = "VisualizerKeysTests"
    private let keyL: UInt16 = 37
    private let keyN: UInt16 = 45
    private let keyF: UInt16 = 3
    private let space: UInt16 = 49

    override func setUp() {
        defaults = UserDefaults(suiteName: suite)
        defaults.removePersistentDomain(forName: suite)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
    }

    func testDefaultsAreLAndN() {
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertEqual(keys.like, keyL)
        XCTAssertEqual(keys.skip, keyN)
        XCTAssertEqual(keys.codes, [keyL, keyN])
    }

    func testReadsTheStoredKeys() {
        defaults.set(Int(keyF), forKey: VisualizerKeys.likeKey)
        defaults.set(Int(space), forKey: VisualizerKeys.skipKey)
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertEqual(keys.action(for: keyF), .like)
        XCTAssertEqual(keys.action(for: space), .skip)
        XCTAssertNil(keys.action(for: keyL))
    }

    func testOffMeansNoKey() {
        defaults.set(VisualizerKeys.off, forKey: VisualizerKeys.likeKey)
        defaults.set(VisualizerKeys.off, forKey: VisualizerKeys.skipKey)
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertNil(keys.like)
        XCTAssertNil(keys.skip)
        XCTAssertTrue(keys.codes.isEmpty)
        XCTAssertNil(keys.action(for: keyL))
    }

    func testAKeyThatIsNotOfferedIsTheDefault() {
        // 53 is Escape, 100000 isn't a key code at all.
        defaults.set(53, forKey: VisualizerKeys.likeKey)
        defaults.set(100_000, forKey: VisualizerKeys.skipKey)
        XCTAssertEqual(VisualizerKeys(defaults: defaults), VisualizerKeys())
        defaults.set("L", forKey: VisualizerKeys.likeKey)
        XCTAssertEqual(VisualizerKeys(defaults: defaults).like, keyL)
    }

    func testOneKeyNeverDoesBoth() {
        defaults.set(Int(keyF), forKey: VisualizerKeys.likeKey)
        defaults.set(Int(keyF), forKey: VisualizerKeys.skipKey)
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertEqual(keys.action(for: keyF), .like)
        XCTAssertNil(keys.skip)
    }

    func testChoicesAreLettersDigitsArrowsAndSpace() {
        let choices = VisualizerKey.choices
        XCTAssertEqual(choices.count, 26 + 10 + 4 + 1)
        XCTAssertEqual(Set(choices.map(\.code)).count, choices.count)
        XCTAssertEqual(Set(choices.map(\.label)).count, choices.count)
        XCTAssertEqual(choices.first { $0.code == keyL }?.label, "L")
        XCTAssertEqual(choices.first { $0.code == 124 }?.label, "→")
        XCTAssertTrue(choices.contains { $0.code == UInt16(VisualizerKeys.defaultLike) })
        XCTAssertTrue(choices.contains { $0.code == UInt16(VisualizerKeys.defaultSkip) })
    }

    func testScripts() {
        XCTAssertEqual(VisualizerKeys.skipScript, "window.skipPreset?.()")
        XCTAssertEqual(VisualizerKeys.likeScript(liked: true), "window.showLike?.(true)")
        XCTAssertEqual(VisualizerKeys.likeScript(liked: false), "window.showLike?.(false)")
    }
}
