import XCTest
@testable import IdleVizCore

final class TimingSettingsTests: XCTestCase {
    private var defaults: UserDefaults!
    private let suite = "TimingSettingsTests"

    override func setUp() {
        defaults = UserDefaults(suiteName: suite)
        defaults.removePersistentDomain(forName: suite)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
    }

    func testDefaults() {
        let settings = TimingSettings(defaults: defaults)
        XCTAssertEqual(settings.idleTimeout(onBattery: false), 300)
        XCTAssertEqual(settings.keepAwakeLimit(onBattery: false), 3600)
        XCTAssertFalse(settings.useBatteryTimes)
    }

    func testBatteryValuesOnlyApplyOnBatteryWithTheSwitchOn() {
        let settings = TimingSettings(
            idleMinutes: 10, keepAwakeMinutes: 60, useBatteryTimes: true, batteryIdleMinutes: 5, batteryKeepAwakeMinutes: 30
        )
        XCTAssertEqual(settings.idleTimeout(onBattery: false), 600)
        XCTAssertEqual(settings.keepAwakeLimit(onBattery: false), 3600)
        XCTAssertEqual(settings.idleTimeout(onBattery: true), 300)
        XCTAssertEqual(settings.keepAwakeLimit(onBattery: true), 1800)

        var off = settings
        off.useBatteryTimes = false
        XCTAssertEqual(off.idleTimeout(onBattery: true), 600)
        XCTAssertEqual(off.keepAwakeLimit(onBattery: true), 3600)
    }

    func testTheIdleTriggerCanBeOffOnBatteryOnly() {
        let settings = TimingSettings(idleMinutes: 10, useBatteryTimes: true, batteryIdleMinutes: 0)
        XCTAssertEqual(settings.idleTimeout(onBattery: false), 600)
        XCTAssertNil(settings.idleTimeout(onBattery: true))
    }

    func testUnsetBatteryValuesAreCopiesOfThePluggedInOnes() {
        defaults.set(15, forKey: IdleTimeoutSetting.key)
        defaults.set(120, forKey: KeepAwakeSetting.key)
        defaults.set(true, forKey: BatteryTimesSetting.enabledKey)
        let settings = TimingSettings(defaults: defaults)
        XCTAssertEqual(settings.idleTimeout(onBattery: true), 900)
        XCTAssertEqual(settings.keepAwakeLimit(onBattery: true), 7200)
    }

    func testReadsStoredBatteryValues() {
        defaults.set(true, forKey: BatteryTimesSetting.enabledKey)
        defaults.set(0, forKey: BatteryTimesSetting.idleTimeoutKey)
        defaults.set(30, forKey: BatteryTimesSetting.keepAwakeKey)
        let settings = TimingSettings(defaults: defaults)
        XCTAssertNil(settings.idleTimeout(onBattery: true))
        XCTAssertEqual(settings.keepAwakeLimit(onBattery: true), 1800)
        XCTAssertEqual(settings.idleTimeout(onBattery: false), 300)
    }

    func testAKeepAwakeLimitOfZeroFallsBack() {
        defaults.set(0, forKey: KeepAwakeSetting.key)
        defaults.set(-5, forKey: BatteryTimesSetting.keepAwakeKey)
        defaults.set(true, forKey: BatteryTimesSetting.enabledKey)
        let settings = TimingSettings(defaults: defaults)
        XCTAssertEqual(settings.keepAwakeLimit(onBattery: false), 3600)
        XCTAssertEqual(settings.keepAwakeLimit(onBattery: true), 3600)
    }

    func testRemainingCountsFromWhenTheWindowOpened() {
        XCTAssertEqual(TimingSettings.remaining(limit: 3600, openedAt: 1000, now: 1600), 3000)
        XCTAssertEqual(TimingSettings.remaining(limit: 3600, openedAt: 1000, now: 4600), 0)
        // A shorter limit applied late (unplugged while open) is already over.
        XCTAssertLessThan(TimingSettings.remaining(limit: 1800, openedAt: 1000, now: 3400), 0)
    }

    func testKeepAwakeLabels() {
        XCTAssertEqual(KeepAwakeSetting.choices.map(KeepAwakeSetting.label), ["30 min", "1 hour", "2 hours", "4 hours"])
        XCTAssertTrue(KeepAwakeSetting.choices.contains(KeepAwakeSetting.defaultMinutes))
    }

    func testPowerSourceTypes() {
        XCTAssertTrue(PowerSourceType.isBattery("Battery Power"))
        XCTAssertFalse(PowerSourceType.isBattery("AC Power"))
        XCTAssertFalse(PowerSourceType.isBattery("UPS Power"))
    }
}

final class CloseReasonTests: XCTestCase {
    func testInputClosesFasterThanTheLimit() {
        XCTAssertLessThanOrEqual(CloseReason.input.fadeSeconds, 0.3)
        XCTAssertGreaterThan(CloseReason.input.fadeSeconds, 0)
        XCTAssertGreaterThan(CloseReason.keepAwakeLimit.fadeSeconds, CloseReason.input.fadeSeconds)
    }

    func testADisplayChangeClosesAtOnce() {
        XCTAssertEqual(CloseReason.displayChanged.fadeSeconds, 0)
    }
}
