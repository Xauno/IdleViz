import Foundation

/// What kind of item Spotify is playing, read from the `spotify url` prefix.
public enum SpotifyItemKind: String, Sendable, Equatable {
    case song
    case podcast
    // swiftlint:disable:next identifier_name
    case ad

    /// `spotify:track:…` and `spotify:local:…` are songs, `spotify:episode:…` is a podcast,
    /// `spotify:ad:…` is an ad. Returns nil for anything else.
    public init?(spotifyURL: String) {
        let parts = spotifyURL.split(separator: ":", maxSplits: 2, omittingEmptySubsequences: false)
        guard parts.count >= 2, parts[0] == "spotify" else { return nil }
        switch parts[1] {
        case "track", "local": self = .song
        case "episode": self = .podcast
        case "ad": self = .ad
        default: return nil
        }
    }
}

/// What the overlay should treat the current item as. Ads are split by what came before them:
/// after a podcast episode an ad is a podcast ad (no overlay), otherwise a music ad (minimal overlay).
public enum SpotifyContent: String, Sendable, Equatable {
    case song
    case podcast
    case musicAd
    case podcastAd
}

public enum SpotifyPlayerState: String, Sendable, Equatable {
    case playing
    case paused
}

/// One snapshot of Spotify's current item, as returned by the AppleScript query.
public struct NowPlaying: Sendable, Equatable {
    public var state: SpotifyPlayerState
    public var name: String
    public var artist: String
    public var album: String
    /// Empty for local files, which have no artwork.
    public var artworkURL: String
    /// Milliseconds, as Spotify reports it.
    public var durationMS: Int
    /// Seconds, as Spotify reports it.
    public var position: Double
    public var spotifyURL: String
    public var kind: SpotifyItemKind

    public var isLocalFile: Bool { spotifyURL.hasPrefix("spotify:local:") }

    public init(
        state: SpotifyPlayerState,
        name: String,
        artist: String,
        album: String,
        artworkURL: String,
        durationMS: Int,
        position: Double,
        spotifyURL: String,
        kind: SpotifyItemKind
    ) {
        self.state = state
        self.name = name
        self.artist = artist
        self.album = album
        self.artworkURL = artworkURL
        self.durationMS = durationMS
        self.position = position
        self.spotifyURL = spotifyURL
        self.kind = kind
    }
}

/// The one AppleScript query, and parsing its reply.
public enum SpotifyQuery {
    /// Fields are joined with ASCII unit separator (character id 31), which can't appear in titles.
    public static let separator: Character = "\u{1F}"

    /// Artwork and URL are read inside `try`, since local files and ads can lack them.
    public static let source = """
    tell application "Spotify"
        with timeout of 2 seconds
            set s to player state as string
            if s is "stopped" then return "stopped"
            set t to current track
            set d to character id 31
            set a to ""
            try
                set a to artwork url of t
            end try
            set u to ""
            try
                set u to spotify url of t
            end try
            return s & d & (name of t) & d & (artist of t) & d & (album of t) & d & a & d & (duration of t) & d & (player position) & d & u
        end timeout
    end tell
    """

    /// Returns nil when there is no current track: state `stopped`, a malformed reply,
    /// an unknown item kind, or an empty URL (or empty name, except for ads).
    public static func parse(_ reply: String) -> NowPlaying? {
        let fields = reply.split(separator: separator, omittingEmptySubsequences: false).map(String.init)
        guard fields.count == 8,
              let state = SpotifyPlayerState(rawValue: fields[0]),
              let kind = SpotifyItemKind(spotifyURL: fields[7]),
              let duration = Double(number: fields[5]),
              let position = Double(number: fields[6])
        else { return nil }
        // Ads may come without a name; the overlay only shows "Advertisement" for them.
        if fields[1].isEmpty && kind != .ad { return nil }
        return NowPlaying(
            state: state,
            name: fields[1],
            artist: fields[2],
            album: fields[3],
            artworkURL: fields[4] == "missing value" ? "" : fields[4],
            durationMS: Int(duration.rounded()),
            position: position,
            spotifyURL: fields[7],
            kind: kind
        )
    }
}

/// Remembers the kind of the last non-ad item, to tell music ads from podcast ads.
public struct AdContext: Sendable, Equatable {
    public private(set) var lastNonAd: SpotifyItemKind?

    public init() {}

    public mutating func content(for kind: SpotifyItemKind) -> SpotifyContent {
        switch kind {
        case .song:
            lastNonAd = .song
            return .song
        case .podcast:
            lastNonAd = .podcast
            return .podcast
        case .ad:
            return lastNonAd == .podcast ? .podcastAd : .musicAd
        }
    }
}

extension Double {
    /// AppleScript turns numbers into text using the system locale, so a decimal comma is possible.
    init?(number text: String) {
        let trimmed = text.trimmingCharacters(in: .whitespaces)
        guard let value = Double(trimmed) ?? Double(trimmed.replacingOccurrences(of: ",", with: ".")) else {
            return nil
        }
        self = value
    }
}
