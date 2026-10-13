import XCTest
@testable import IdleVizCore

final class ResponseSpeedTests: XCTestCase {
    private var defaults: UserDefaults!
    private let suite = "ResponseSpeedTests"
    private let rate = 48_000.0
    private let count = AudioFrame.sampleCount

    override func setUp() {
        defaults = UserDefaults(suiteName: suite)
        defaults.removePersistentDomain(forName: suite)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
    }

    func testDefaultsToNormal() {
        XCTAssertEqual(ResponseSpeedSetting.value(in: defaults), .normal)
    }

    func testReadsAStoredChoice() {
        defaults.set("snappy", forKey: ResponseSpeedSetting.key)
        XCTAssertEqual(ResponseSpeedSetting.value(in: defaults), .snappy)
    }

    func testAnUnknownChoiceIsNormal() {
        defaults.set("warp", forKey: ResponseSpeedSetting.key)
        XCTAssertEqual(ResponseSpeedSetting.value(in: defaults), .normal)
        defaults.set(3, forKey: ResponseSpeedSetting.key)
        XCTAssertEqual(ResponseSpeedSetting.value(in: defaults), .normal)
    }

    func testNormalKeepsTheOriginalAttack() {
        XCTAssertEqual(ResponseSpeed.normal.attack, 0.7)
    }

    func testFasterChoicesAttackHarder() {
        XCTAssertLessThan(ResponseSpeed.normal.attack, ResponseSpeed.snappy.attack)
        XCTAssertLessThan(ResponseSpeed.snappy.attack, ResponseSpeed.instant.attack)
        XCTAssertEqual(ResponseSpeed.instant.attack, 1)
    }

    /// The bass level one frame after a hit that follows silence.
    private func bassAfterOneFrame(_ speed: ResponseSpeed) -> Float {
        var analyzer = AudioAnalyzer()
        analyzer.responseSpeed = speed
        let silence = [Float](repeating: 0, count: count)
        _ = analyzer.analyze(left: silence, right: silence, sampleRate: rate)
        let tone = (0..<count).map { 0.2 * Float(sin(2 * Double.pi * 60 * Double($0) / rate)) }
        return analyzer.analyze(left: tone, right: tone, sampleRate: rate).bass
    }

    func testAHitReachesTheBandsSoonerWhenFaster() {
        let normal = bassAfterOneFrame(.normal)
        let snappy = bassAfterOneFrame(.snappy)
        let instant = bassAfterOneFrame(.instant)
        XCTAssertGreaterThan(normal, 0)
        XCTAssertGreaterThan(snappy, normal)
        XCTAssertGreaterThan(instant, snappy)
    }

    func testTheReleaseDoesNotChange() {
        func level(after speed: ResponseSpeed) -> Float {
            var analyzer = AudioAnalyzer()
            analyzer.responseSpeed = speed
            let tone = (0..<count).map { 0.2 * Float(sin(2 * Double.pi * 60 * Double($0) / rate)) }
            let silence = [Float](repeating: 0, count: count)
            for _ in 0..<120 { _ = analyzer.analyze(left: tone, right: tone, sampleRate: rate) }
            return analyzer.analyze(left: silence, right: silence, sampleRate: rate).bass
        }
        XCTAssertEqual(level(after: .instant), level(after: .normal), accuracy: 0.001)
    }
}
