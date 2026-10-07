import XCTest
@testable import IdleVizCore

final class VisualizerKeysTests: XCTestCase {
    private var defaults: UserDefaults!
    private let suite = "VisualizerKeysTests"
    private let keyL: UInt16 = 37
    private let keyN: UInt16 = 45
    private let keyB: UInt16 = 11
    private let keyF5: UInt16 = 96
    private let keyF: UInt16 = 3
    private let space: UInt16 = 49

    override func setUp() {
        defaults = UserDefaults(suiteName: suite)
        defaults.removePersistentDomain(forName: suite)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
    }

    func testDefaultsAreLNAndB() {
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertEqual(keys.like, keyL)
        XCTAssertEqual(keys.skip, keyN)
        XCTAssertEqual(keys.block, keyB)
        XCTAssertEqual(keys.codes, [keyL, keyN, keyB])
    }

    func testReadsTheStoredKeys() {
        defaults.set(Int(keyF), forKey: VisualizerKeys.likeKey)
        defaults.set(Int(space), forKey: VisualizerKeys.skipKey)
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertEqual(keys.action(for: keyF), .like)
        XCTAssertEqual(keys.action(for: space), .skip)
        XCTAssertEqual(keys.action(for: keyB), .block)
        XCTAssertNil(keys.action(for: keyL))
        defaults.set(Int(keyF5), forKey: VisualizerKeys.blockKey)
        XCTAssertEqual(VisualizerKeys(defaults: defaults).action(for: keyF5), .block)
    }

    func testOffMeansNoKey() {
        defaults.set(VisualizerKeys.off, forKey: VisualizerKeys.likeKey)
        defaults.set(VisualizerKeys.off, forKey: VisualizerKeys.skipKey)
        defaults.set(VisualizerKeys.off, forKey: VisualizerKeys.blockKey)
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertNil(keys.like)
        XCTAssertNil(keys.skip)
        XCTAssertTrue(keys.codes.isEmpty)
        XCTAssertNil(keys.action(for: keyL))
    }

    func testAStoredValueThatCannotBeAKeyIsTheDefault() {
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

    func testAnOlderChoiceOfBForLikeOrSkipKeepsItAndLeavesBlockUnset() {
        defaults.set(Int(keyB), forKey: VisualizerKeys.skipKey)
        let keys = VisualizerKeys(defaults: defaults)
        XCTAssertEqual(keys.action(for: keyB), .skip)
        XCTAssertNil(keys.block)
        XCTAssertEqual(VisualizerKeys(like: keyF, skip: keyL, block: keyF).block, nil)
    }

    func testAlmostAnyKeyCanBeRecorded() {
        for code in [0, 11, 36, 49, 96, 123, 127] { XCTAssertTrue(VisualizerKeys.canBe(code), "\(code)") }
        // Esc cancels the recorder; Command, Shift, Caps Lock, Option, Control and fn are keys of their own.
        for code in [53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, -1, 128, 100_000] {
            XCTAssertFalse(VisualizerKeys.canBe(code), "\(code)")
        }
    }

    func testKeysAreNamedAsOnAUSKeyboard() {
        XCTAssertEqual(VisualizerKeys.label(keyL), "L")
        XCTAssertEqual(VisualizerKeys.label(space), "Space")
        XCTAssertEqual(VisualizerKeys.label(keyF5), "F5")
        XCTAssertEqual(VisualizerKeys.label(29), "0")
        XCTAssertEqual(VisualizerKeys.label(92), "Num 9")
        XCTAssertEqual(VisualizerKeys.label(124), "→")
        XCTAssertEqual(VisualizerKeys.label(42), "\\")
        XCTAssertEqual(VisualizerKeys.label(110), "Key 110")
        XCTAssertEqual(VisualizerKeys.label(nil), "Not set")
        // No two keys share a name.
        let named = (0..<128).map { VisualizerKeys.label(UInt16($0)) }.filter { !$0.hasPrefix("Key ") }
        XCTAssertEqual(Set(named).count, named.count)
    }

    func testScripts() {
        XCTAssertEqual(VisualizerKeys.skipScript, "window.skipPreset?.()")
        XCTAssertEqual(VisualizerKeys.likeScript(liked: true), "window.showLike?.(true)")
        XCTAssertEqual(VisualizerKeys.likeScript(liked: false), "window.showLike?.(false)")
    }
}
