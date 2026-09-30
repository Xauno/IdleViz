import XCTest
@testable import IdleVizCore

final class ActivationStatsTests: XCTestCase {
    func testCountsOpensAndRefusals() {
        var stats = ActivationStats()
        stats.record(activated: true)
        stats.record(activated: false)
        stats.record(activated: true)
        XCTAssertEqual(stats.opens, 3)
        XCTAssertEqual(stats.refused, 1)
        XCTAssertEqual(stats.summary, "activation refused 1 of 3 opens")
    }
}
