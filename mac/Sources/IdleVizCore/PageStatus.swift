import Foundation

/// The page's answer to `window.idlevizStatus()`. The page runs preset and plugin code,
/// so the reply is untrusted: only the expected shape is read, and strings are cut short.
public struct PageStatus: Sendable, Equatable {
    public static let maxTextLength = 300
    public static let maxFailures = 200

    /// The id of the preset on screen, or nil if none is.
    public var preset: String?
    /// Frames rendered since the page loaded.
    public var frames: Int
    /// Audio frames the page has received since it loaded.
    public var audioFrames: Int
    /// Presets and plugins that failed to load, each with its error message.
    public var failed: [PresetFailure]

    public init(preset: String?, frames: Int, audioFrames: Int, failed: [PresetFailure]) {
        self.preset = preset
        self.frames = frames
        self.audioFrames = audioFrames
        self.failed = failed
    }

    /// Reads what `evaluateJavaScript` returned. Nil if it isn't a status object at all.
    public init?(reply: Any?) {
        guard let reply = reply as? [String: Any] else { return nil }
        preset = (reply["preset"] as? String).map(Self.clip)
        frames = Self.count(reply["frames"])
        audioFrames = Self.count(reply["audioFrames"])
        failed = (reply["failed"] as? [Any] ?? []).prefix(Self.maxFailures).compactMap { item in
            guard let item = item as? [String: Any], let id = item["id"] as? String, !id.isEmpty else { return nil }
            return PresetFailure(id: Self.clip(id), error: Self.clip(item["error"] as? String ?? "Failed to load"))
        }
    }

    /// Cuts text from the page down to a length that is safe to log and show.
    public static func clip(_ text: String) -> String {
        String(text.prefix(maxTextLength))
    }

    private static func count(_ value: Any?) -> Int {
        guard let number = value as? Double, number.isFinite, number >= 0, number < 1e15 else { return 0 }
        return Int(number)
    }
}

/// One preset or plugin that couldn't be loaded, for the "Failed to load" list in settings.
public struct PresetFailure: Sendable, Equatable, Identifiable {
    public var id: String
    public var error: String

    public init(id: String, error: String) {
        self.id = id
        self.error = error
    }
}

/// Decides when the page has stopped answering. WebKit may run a plugin on the same thread as
/// the page, so a plugin stuck in a loop freezes all of it; the only cure is a reload.
public struct PageWatchdog: Sendable {
    public static let reloadAfterSeconds: TimeInterval = 3

    private var lastReply: TimeInterval?

    public init() {}

    /// Call when the status checks start (the window opened, or the page was reloaded).
    public mutating func start(at now: TimeInterval) {
        lastReply = now
    }

    public mutating func stop() {
        lastReply = nil
    }

    public mutating func replied(at now: TimeInterval) {
        if lastReply != nil { lastReply = now }
    }

    /// Call before each status check. True means reload the page; the count then starts over.
    public mutating func shouldReload(at now: TimeInterval) -> Bool {
        guard let lastReply, now - lastReply >= Self.reloadAfterSeconds else { return false }
        self.lastReply = now
        return true
    }
}
