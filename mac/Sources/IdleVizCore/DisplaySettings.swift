import Foundation

/// The "Brightness" setting: how much of the visualizer shows through the black dim layer.
public enum BrightnessSetting {
    public static let key = "visualizerBrightness"
    public static let defaultValue = 0.7
    public static let range = 0.5...1.0

    /// A brightness inside the slider's range, in whole percent.
    public static func normalized(_ value: Double) -> Double {
        guard value.isFinite else { return defaultValue }
        let clamped = min(max(value, range.lowerBound), range.upperBound)
        return (clamped * 100).rounded() / 100
    }

    public static func label(_ value: Double) -> String {
        "\(Int((normalized(value) * 100).rounded()))%"
    }

    /// Reads the stored value. A value that was never set, or isn't a number, is the default.
    public static func value(in defaults: UserDefaults) -> Double {
        normalized((defaults.object(forKey: key) as? NSNumber)?.doubleValue ?? defaultValue)
    }

    public static func script(for value: Double) -> String {
        "window.setBrightness?.(\(normalized(value)))"
    }
}

/// The "Show Spotify overlay" switch. Off means the page shows only the visualizer and the dim layer.
public enum OverlaySetting {
    public static let key = "showOverlay"

    /// On unless it was switched off.
    public static func value(in defaults: UserDefaults) -> Bool {
        defaults.object(forKey: key) as? Bool ?? true
    }

    public static func script(for enabled: Bool) -> String {
        "window.setOverlayEnabled?.(\(enabled))"
    }
}

/// The "Show visualizer title" switch: the name of the preset on screen, small, in the top left corner.
public enum PresetTitleSetting {
    public static let key = "showPresetTitle"

    /// Off unless it was switched on.
    public static func value(in defaults: UserDefaults) -> Bool {
        defaults.object(forKey: key) as? Bool ?? false
    }

    public static func script(for enabled: Bool) -> String {
        "window.setPresetTitleEnabled?.(\(enabled))"
    }
}
