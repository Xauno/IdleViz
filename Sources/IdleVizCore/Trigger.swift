import Foundation

/// What asked the visualizer to open.
public enum TriggerSource: String, Sendable, Equatable {
    case hotkey
    case urlScheme
    case settings
    case idle

    /// Manual triggers mean someone is at the Mac: they skip the idle skip rules
    /// and flash the menu-bar icon when the open is refused.
    public var isManual: Bool { self != .idle }
}

/// Commands accepted on the external `idleviz://` URL scheme.
public enum URLCommand: Sendable, Equatable {
    case open

    public static let scheme = "idleviz"

    public init?(url: URL) {
        guard url.scheme?.lowercased() == Self.scheme else { return nil }
        switch url.host()?.lowercased() {
        case "open": self = .open
        default: return nil
        }
    }
}
