import Foundation

/// What macOS says about one permission.
public enum PermissionState: String, Sendable, Equatable {
    case granted
    case denied
    /// macOS hasn't asked yet. The next use would show its prompt.
    case notAsked
    /// Can't be told right now (Spotify isn't running, or the check isn't available).
    case unknown

    /// Denied or never asked. Unknown doesn't count: nothing is known to be wrong.
    public var isMissing: Bool { self == .denied || self == .notAsked }
}

/// The two permissions the app can't work without. The microphone is optional and isn't one of them.
public enum RequiredPermission: String, Sendable, Equatable, CaseIterable, Identifiable {
    /// Automation: asking Spotify what's playing.
    case automation
    /// System Audio Recording: the process tap on Spotify.
    case audio

    public var id: String { rawValue }

    /// The error row in the menu-bar popup.
    public var errorTitle: String {
        switch self {
        case .automation: "Spotify control not allowed"
        case .audio: "Spotify audio blocked"
        }
    }

    /// The row title in the welcome window.
    public var name: String {
        switch self {
        case .automation: "Spotify control"
        case .audio: "Spotify audio"
        }
    }

    public var explanation: String {
        switch self {
        case .automation: "To read what's playing. IdleViz never changes playback."
        case .audio: "To make the visuals move. IdleViz hears Spotify only."
        }
    }

    /// The System Settings privacy page where the permission is switched on.
    public var settingsURL: URL {
        switch self {
        case .automation: URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!
        // The "Screen & System Audio Recording" page.
        case .audio: URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!
        }
    }
}

/// Both required permissions, as last checked.
public struct PermissionStatus: Sendable, Equatable {
    public var automation: PermissionState
    public var audio: PermissionState

    public init(automation: PermissionState = .unknown, audio: PermissionState = .unknown) {
        self.automation = automation
        self.audio = audio
    }

    public subscript(permission: RequiredPermission) -> PermissionState {
        get { permission == .automation ? automation : audio }
        set {
            if permission == .automation { automation = newValue } else { audio = newValue }
        }
    }

    /// The permissions that turn the menu-bar icon yellow and get an error row, in row order.
    public var missing: [RequiredPermission] {
        RequiredPermission.allCases.filter { self[$0].isMissing }
    }

    /// The welcome window asks for permissions macOS hasn't asked about yet. A denied one can
    /// only be changed in System Settings, so it doesn't bring the window up.
    public var needsWelcome: Bool {
        RequiredPermission.allCases.contains { self[$0] == .notAsked }
    }

    /// What clicking a missing permission's error row does.
    public func action(for permission: RequiredPermission) -> PermissionAction {
        self[permission] == .notAsked ? .showWelcome : .openSettings
    }

    // MARK: Reading what macOS reports

    /// From `AEDeterminePermissionToAutomateTarget`. Spotify has to be running for an answer,
    /// so anything else keeps what was known before.
    public static func automation(status: Int32, previous: PermissionState) -> PermissionState {
        switch status {
        case 0: .granted
        case -1743: .denied // errAEEventNotPermitted
        case -1744: .notAsked // errAEEventWouldRequireUserConsent
        default: previous // -600 (procNotFound): Spotify isn't running
        }
    }

    /// From the private `TCCAccessPreflight` for audio capture, or nil when that function is gone.
    /// Without it the only sign is a tap that hears nothing while Spotify plays (`TapHealth`).
    public static func audio(preflight: Int32?, tapSuspected: Bool) -> PermissionState {
        switch preflight {
        case 0: .granted
        case 1: .denied
        case 2: .notAsked
        default: tapSuspected ? .denied : .unknown
        }
    }
}

public enum PermissionAction: Sendable, Equatable {
    /// Never asked: the welcome window can show the macOS prompt.
    case showWelcome
    /// Denied: only System Settings can change it.
    case openSettings
}
