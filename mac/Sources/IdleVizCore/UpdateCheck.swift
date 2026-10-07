import Foundation

/// A release version such as 0.1.2, read from the app's own version or a release tag ("v0.1.2").
public struct AppVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    /// The numbers without trailing zeros, so 0.1 and 0.1.0 are the same version.
    private let parts: [Int]

    /// Nil for anything but one to four whole numbers with dots between them, with or without a "v"
    /// in front. A tag like "v0.2.0-beta" is nil too, so a test release is never offered.
    public init?(_ text: String) {
        var text = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.first == "v" || text.first == "V" { text.removeFirst() }
        let fields = text.split(separator: ".", omittingEmptySubsequences: false)
        guard (1...4).contains(fields.count) else { return nil }
        var parts: [Int] = []
        for field in fields {
            guard !field.isEmpty, field.count <= 9, field.allSatisfy({ $0.isASCII && $0.isNumber }), let number = Int(field)
            else { return nil }
            parts.append(number)
        }
        while parts.count > 1, parts.last == 0 { parts.removeLast() }
        self.parts = parts
    }

    public static func < (lhs: AppVersion, rhs: AppVersion) -> Bool {
        lhs.parts.lexicographicallyPrecedes(rhs.parts)
    }

    /// At least three numbers, as the releases are named: "0.2.0".
    public var description: String {
        (parts + Array(repeating: 0, count: max(0, 3 - parts.count))).map(String.init).joined(separator: ".")
    }
}

/// The check for a newer release: once a day the app asks GitHub for the latest one and, if it is
/// newer than the running copy, the menu-bar popup offers it. Nothing is downloaded or installed.
public enum UpdateCheck {
    /// The "Check for updates" switch.
    public static let enabledKey = "checkForUpdates"
    /// When GitHub last answered, in seconds since 1970.
    public static let lastCheckKey = "updateLastCheck"
    /// The latest release GitHub named then, kept so the row is there again right after a restart.
    public static let latestKey = "updateLatestVersion"

    public static let interval: TimeInterval = 24 * 60 * 60
    /// How long to wait after a check that failed, for example with no network.
    public static let retryInterval: TimeInterval = 60 * 60

    public static let latestRelease = URL(string: "https://api.github.com/repos/Xauno/IdleViz/releases/latest")!
    /// Where the row sends the user. Fixed, so nothing GitHub answers decides which page opens.
    public static let releasePage = URL(string: "https://github.com/Xauno/IdleViz/releases/latest")!

    /// On unless it was switched off.
    public static func isEnabled(in defaults: UserDefaults) -> Bool {
        defaults.object(forKey: enabledKey) as? Bool ?? true
    }

    /// The version in GitHub's answer about the latest release. Nil if the answer can't be read,
    /// or names a draft, a prerelease or a tag that isn't a plain version.
    public static func latestVersion(in data: Data) -> AppVersion? {
        guard let release = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = release["tag_name"] as? String,
              release["draft"] as? Bool != true, release["prerelease"] as? Bool != true
        else { return nil }
        return AppVersion(tag)
    }

    /// Seconds until the next check is due; 0 means now. A last check in the future (the clock
    /// was set back) counts as never.
    public static func wait(lastCheck: Date?, now: Date) -> TimeInterval {
        guard let lastCheck, lastCheck <= now else { return 0 }
        return max(0, interval - now.timeIntervalSince(lastCheck))
    }

    /// The version to offer: the latest release, if it is newer than the running copy.
    public static func available(installed: AppVersion?, latest: AppVersion?) -> AppVersion? {
        guard let installed, let latest, latest > installed else { return nil }
        return latest
    }
}
