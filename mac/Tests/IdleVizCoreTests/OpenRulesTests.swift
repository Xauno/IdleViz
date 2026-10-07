import XCTest
@testable import IdleVizCore

final class OpenRulesTests: XCTestCase {
    private func snapshot(_ content: SpotifyContent, state: SpotifyPlayerState = .playing) -> SpotifySnapshot {
        let kind: SpotifyItemKind = switch content {
        case .song: .song
        case .podcast: .podcast
        case .musicAd, .podcastAd: .ad
        }
        let item = NowPlaying(
            state: state, name: "Name", artist: "Artist", album: "Album", artworkURL: "",
            durationMS: 1000, position: 0, spotifyURL: "spotify:track:a", kind: kind
        )
        return SpotifySnapshot(nowPlaying: item, content: content)
    }

    func testRefusesWhenSpotifyIsNotRunning() {
        XCTAssertEqual(OpenRules.refusal(spotifyRunning: false, snapshot: nil), .spotifyNotRunning)
        // A stale snapshot doesn't count once Spotify is gone.
        XCTAssertEqual(OpenRules.refusal(spotifyRunning: false, snapshot: snapshot(.song)), .spotifyNotRunning)
    }

    func testRefusesWithoutATrack() {
        XCTAssertEqual(OpenRules.refusal(spotifyRunning: true, snapshot: nil), .noTrack)
    }

    func testOpensForEveryKindOfTrack() {
        for content in [SpotifyContent.song, .podcast, .musicAd, .podcastAd] {
            XCTAssertNil(OpenRules.refusal(spotifyRunning: true, snapshot: snapshot(content)), "\(content)")
        }
        XCTAssertNil(OpenRules.refusal(spotifyRunning: true, snapshot: snapshot(.song, state: .paused)))
    }
}
