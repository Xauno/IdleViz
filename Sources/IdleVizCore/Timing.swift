import Foundation

/// The "Keep screen awake" setting, stored in minutes.
public enum KeepAwakeSetting {
    public static let key = "keepAwakeLimit"
    public static let defaultMinutes = 60
    public static let choices = [30, 60, 120, 240]

    public static func label(minutes: Int) -> String {
        if minutes < 60 { return "\(minutes) min" }
        return minutes == 60 ? "1 hour" : "\(minutes / 60) hours"
    }
}

/// The "Different times on battery" switch and the two values it reveals, stored in minutes.
public enum BatteryTimesSetting {
    public static let enabledKey = "useBatteryTimes"
    public static let idleTimeoutKey = "idleTimeoutBattery"
    public static let keepAwakeKey = "keepAwakeLimitBattery"
}

/// What `IOPSGetProvidingPowerSourceType` reports.
public enum PowerSourceType {
    /// A UPS doesn't count: it's a desktop on mains that is about to lose them, not a laptop unplugged.
    public static func isBattery(_ type: String) -> Bool {
        type == "Battery Power"
    }
}

/// The idle timeout and the keep-awake limit, with the values that replace them on battery.
public struct TimingSettings: Sendable, Equatable {
    /// 0 means the idle trigger is off.
    public var idleMinutes: Int
    public var keepAwakeMinutes: Int
    public var useBatteryTimes: Bool
    public var batteryIdleMinutes: Int
    public var batteryKeepAwakeMinutes: Int

    public init(
        idleMinutes: Int = IdleTimeoutSetting.defaultMinutes,
        keepAwakeMinutes: Int = KeepAwakeSetting.defaultMinutes,
        useBatteryTimes: Bool = false,
        batteryIdleMinutes: Int? = nil,
        batteryKeepAwakeMinutes: Int? = nil
    ) {
        self.idleMinutes = idleMinutes
        self.keepAwakeMinutes = keepAwakeMinutes
        self.useBatteryTimes = useBatteryTimes
        self.batteryIdleMinutes = batteryIdleMinutes ?? idleMinutes
        self.batteryKeepAwakeMinutes = batteryKeepAwakeMinutes ?? keepAwakeMinutes
    }

    /// Reads the stored settings. A battery value that was never set is a copy of the plugged-in one.
    public init(defaults: UserDefaults) {
        // A keep-awake limit of zero or less would close the window the moment it opens.
        func limit(_ key: String) -> Int? {
            (defaults.object(forKey: key) as? Int).flatMap { $0 > 0 ? $0 : nil }
        }
        self.init(
            idleMinutes: defaults.object(forKey: IdleTimeoutSetting.key) as? Int ?? IdleTimeoutSetting.defaultMinutes,
            keepAwakeMinutes: limit(KeepAwakeSetting.key) ?? KeepAwakeSetting.defaultMinutes,
            useBatteryTimes: defaults.bool(forKey: BatteryTimesSetting.enabledKey),
            batteryIdleMinutes: defaults.object(forKey: BatteryTimesSetting.idleTimeoutKey) as? Int,
            batteryKeepAwakeMinutes: limit(BatteryTimesSetting.keepAwakeKey)
        )
    }

    private func batteryTimesApply(_ onBattery: Bool) -> Bool {
        useBatteryTimes && onBattery
    }

    /// Seconds of no input before opening, or nil when the idle trigger is off.
    public func idleTimeout(onBattery: Bool) -> TimeInterval? {
        IdleTimeoutSetting.timeout(minutes: batteryTimesApply(onBattery) ? batteryIdleMinutes : idleMinutes)
    }

    /// How long the open visualizer keeps the display awake, in seconds.
    public func keepAwakeLimit(onBattery: Bool) -> TimeInterval {
        TimeInterval((batteryTimesApply(onBattery) ? batteryKeepAwakeMinutes : keepAwakeMinutes) * 60)
    }

    /// Seconds until the limit is reached, counted from when the window opened. Zero or less means close now.
    public static func remaining(limit: TimeInterval, openedAt: TimeInterval, now: TimeInterval) -> TimeInterval {
        limit - (now - openedAt)
    }
}
