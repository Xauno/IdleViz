import Foundation

/// Why the visualizer didn't open.
public enum OpenRefusal: String, Sendable, Equatable {
    case spotifyNotRunning
    case noTrack
}

/// The visualizer only opens while Spotify is running and has a current track, playing or paused.
/// Podcasts and ads count as tracks; the overlay decides what to show for them.
public enum OpenRules {
    /// Returns nil when the visualizer may open.
    public static func refusal(spotifyRunning: Bool, snapshot: SpotifySnapshot?) -> OpenRefusal? {
        guard spotifyRunning else { return .spotifyNotRunning }
        guard snapshot != nil else { return .noTrack }
        return nil
    }
}
