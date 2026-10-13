import Foundation

/// The "Response speed" setting (experimental): how fast the plugin bands follow a rising level.
/// Butterchurn presets are not affected, since they get the raw waveform and analyse it themselves.
public enum ResponseSpeed: String, CaseIterable, Sendable {
    case normal
    case snappy
    case instant

    /// How far a band moves towards a higher value each frame. At 60 frames a second, `normal` takes
    /// about 3 frames to get within 3% of a hit, `snappy` 2 and `instant` 1.
    public var attack: Float {
        switch self {
        case .normal: 0.7
        case .snappy: 0.9
        case .instant: 1
        }
    }

    public var label: String {
        switch self {
        case .normal: "Normal"
        case .snappy: "Snappy"
        case .instant: "Instant"
        }
    }
}

public enum ResponseSpeedSetting {
    public static let key = "responseSpeed"
    public static let defaultValue = ResponseSpeed.normal

    /// Reads the stored choice. One that was never set, or isn't a known choice, is the default.
    public static func value(in defaults: UserDefaults) -> ResponseSpeed {
        (defaults.string(forKey: key)).flatMap(ResponseSpeed.init(rawValue:)) ?? defaultValue
    }
}
