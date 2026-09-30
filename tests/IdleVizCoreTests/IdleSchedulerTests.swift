import XCTest
@testable import IdleVizCore

final class IdleSchedulerTests: XCTestCase {
    func testOffWithoutATimeout() {
        var scheduler = IdleScheduler(timeout: nil)
        XCTAssertEqual(scheduler.check(now: 1000, idle: 5000), .off)
        scheduler.timeout = 0
        XCTAssertEqual(scheduler.check(now: 1000, idle: 5000), .off)
    }

    func testWaitsUntilTheTimeoutCouldBeReached() {
        var scheduler = IdleScheduler(timeout: 300)
        XCTAssertEqual(scheduler.check(now: 1000, idle: 120), .wait(180))
    }

    func testFiresOnceTheTimeoutIsReached() {
        var scheduler = IdleScheduler(timeout: 300)
        XCTAssertEqual(scheduler.check(now: 1000, idle: 300), .fire)
    }

    func testOneAttemptPerIdlePeriod() {
        var scheduler = IdleScheduler(timeout: 300)
        XCTAssertEqual(scheduler.check(now: 1000, idle: 300), .fire)
        // Still idle since uptime 700: wait a full timeout, the earliest a new period could fire.
        XCTAssertEqual(scheduler.check(now: 1300, idle: 600), .wait(300))
        XCTAssertEqual(scheduler.check(now: 1300.2, idle: 600.4), .wait(300))
    }

    func testNewInputStartsANewPeriod() {
        var scheduler = IdleScheduler(timeout: 300)
        XCTAssertEqual(scheduler.check(now: 1000, idle: 300), .fire)
        // Input at uptime 1100, then idle again.
        XCTAssertEqual(scheduler.check(now: 1300, idle: 200), .wait(100))
        XCTAssertEqual(scheduler.check(now: 1400, idle: 300), .fire)
    }

    func testChangingTheTimeoutAppliesAtOnce() {
        var scheduler = IdleScheduler(timeout: 600)
        XCTAssertEqual(scheduler.check(now: 1000, idle: 400), .wait(200))
        scheduler.timeout = 300
        XCTAssertEqual(scheduler.check(now: 1000, idle: 400), .fire)
    }
}

final class IdleSkipRulesTests: XCTestCase {
    private let ignored: Set<Int32> = [100, 200]

    private func conditions(locked: Bool = false, console: Bool = true, _ assertions: [PowerAssertion] = []) -> IdleConditions {
        IdleConditions(screenLocked: locked, onConsole: console, assertions: assertions)
    }

    func testOpensWhenNothingBlocks() {
        XCTAssertNil(IdleSkipRules.skip(conditions(), ignoredPIDs: ignored))
    }

    func testSkipsWhileLocked() {
        XCTAssertEqual(IdleSkipRules.skip(conditions(locked: true), ignoredPIDs: ignored), .screenLocked)
    }

    func testSkipsWhenAnotherUserIsOnTheConsole() {
        XCTAssertEqual(IdleSkipRules.skip(conditions(console: false), ignoredPIDs: ignored), .notOnConsole)
    }

    func testSkipsWhileAnotherAppKeepsTheDisplayAwake() {
        let video = PowerAssertion(pid: 300, type: "PreventUserIdleDisplaySleep", processName: "Safari")
        XCTAssertEqual(IdleSkipRules.skip(conditions([video]), ignoredPIDs: ignored), .displayKeptAwake(holder: "Safari"))
        let legacy = PowerAssertion(pid: 301, type: "NoDisplaySleepAssertion", processName: "zoom.us")
        XCTAssertEqual(IdleSkipRules.skip(conditions([legacy]), ignoredPIDs: ignored), .displayKeptAwake(holder: "zoom.us"))
    }

    func testIgnoresSystemSleepAssertions() {
        let audio = PowerAssertion(pid: 418, type: "PreventUserIdleSystemSleep", processName: "coreaudiod")
        let electron = PowerAssertion(pid: 500, type: "NoIdleSleepAssertion", processName: "Claude")
        XCTAssertNil(IdleSkipRules.skip(conditions([audio, electron]), ignoredPIDs: ignored))
    }

    func testIgnoresSpotifyAndItself() {
        let spotify = PowerAssertion(pid: 100, type: "PreventUserIdleDisplaySleep", processName: "Spotify")
        let forSpotify = PowerAssertion(pid: 418, onBehalfOf: 200, type: "PreventUserIdleDisplaySleep", processName: "coreaudiod")
        XCTAssertNil(IdleSkipRules.skip(conditions([spotify, forSpotify]), ignoredPIDs: ignored))
    }
}

final class IdleTimeoutSettingTests: XCTestCase {
    func testMinutesToSeconds() {
        XCTAssertEqual(IdleTimeoutSetting.timeout(minutes: 5), 300)
        XCTAssertNil(IdleTimeoutSetting.timeout(minutes: 0))
        XCTAssertTrue(IdleTimeoutSetting.choices.contains(IdleTimeoutSetting.defaultMinutes))
    }
}
