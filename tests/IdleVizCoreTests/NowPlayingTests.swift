import XCTest
@testable import IdleVizCore

final class NowPlayingTests: XCTestCase {
    private func reply(_ fields: [String]) -> String {
        fields.joined(separator: String(SpotifyQuery.separator))
    }

    private let song = [
        "playing", "Instant Crush", "Daft Punk", "Random Access Memories",
        "https://i.scdn.co/image/abc", "337560", "12.5", "spotify:track:2cGxRwrMyEAp8dEbuZaVv6",
    ]

    func testItemKindFromURL() {
        XCTAssertEqual(SpotifyItemKind(spotifyURL: "spotify:track:abc"), .song)
        XCTAssertEqual(SpotifyItemKind(spotifyURL: "spotify:local:Artist:Album:Title:215"), .song)
        XCTAssertEqual(SpotifyItemKind(spotifyURL: "spotify:episode:abc"), .podcast)
        XCTAssertEqual(SpotifyItemKind(spotifyURL: "spotify:ad:abc"), .ad)
        XCTAssertNil(SpotifyItemKind(spotifyURL: "spotify:show:abc"))
        XCTAssertNil(SpotifyItemKind(spotifyURL: "https://open.spotify.com/track/abc"))
        XCTAssertNil(SpotifyItemKind(spotifyURL: ""))
    }

    func testParsesSong() throws {
        let nowPlaying = try XCTUnwrap(SpotifyQuery.parse(reply(song)))
        XCTAssertEqual(nowPlaying.state, .playing)
        XCTAssertEqual(nowPlaying.name, "Instant Crush")
        XCTAssertEqual(nowPlaying.artist, "Daft Punk")
        XCTAssertEqual(nowPlaying.album, "Random Access Memories")
        XCTAssertEqual(nowPlaying.artworkURL, "https://i.scdn.co/image/abc")
        XCTAssertEqual(nowPlaying.durationMS, 337_560)
        XCTAssertEqual(nowPlaying.position, 12.5)
        XCTAssertEqual(nowPlaying.kind, .song)
        XCTAssertFalse(nowPlaying.isLocalFile)
    }

    func testParsesPaused() throws {
        var fields = song
        fields[0] = "paused"
        XCTAssertEqual(try XCTUnwrap(SpotifyQuery.parse(reply(fields))).state, .paused)
    }

    func testTitlesMayContainOldSeparator() throws {
        var fields = song
        fields[1] = "A || B"
        XCTAssertEqual(try XCTUnwrap(SpotifyQuery.parse(reply(fields))).name, "A || B")
    }

    func testDecimalCommaFromLocale() throws {
        var fields = song
        fields[6] = "332,779998779297"
        XCTAssertEqual(try XCTUnwrap(SpotifyQuery.parse(reply(fields))).position, 332.779998779297, accuracy: 1e-9)
    }

    func testLocalFileHasNoArtwork() throws {
        var fields = song
        fields[4] = "missing value"
        fields[7] = "spotify:local:Artist:Album:Title:215"
        let nowPlaying = try XCTUnwrap(SpotifyQuery.parse(reply(fields)))
        XCTAssertEqual(nowPlaying.artworkURL, "")
        XCTAssertTrue(nowPlaying.isLocalFile)
    }

    func testAdMayHaveNoName() throws {
        var fields = song
        fields[1] = ""
        fields[7] = "spotify:ad:abc"
        XCTAssertEqual(try XCTUnwrap(SpotifyQuery.parse(reply(fields))).kind, .ad)
    }

    func testNoCurrentTrack() {
        XCTAssertNil(SpotifyQuery.parse("stopped"))
        XCTAssertNil(SpotifyQuery.parse(""))
        var noName = song
        noName[1] = ""
        XCTAssertNil(SpotifyQuery.parse(reply(noName)))
        var noURL = song
        noURL[7] = ""
        XCTAssertNil(SpotifyQuery.parse(reply(noURL)))
        var badState = song
        badState[0] = "stopped"
        XCTAssertNil(SpotifyQuery.parse(reply(badState)))
        XCTAssertNil(SpotifyQuery.parse(reply(Array(song.dropLast()))))
    }

    func testAdContext() {
        var context = AdContext()
        XCTAssertEqual(context.content(for: .ad), .musicAd)
        XCTAssertEqual(context.content(for: .song), .song)
        XCTAssertEqual(context.content(for: .ad), .musicAd)
        XCTAssertEqual(context.content(for: .podcast), .podcast)
        XCTAssertEqual(context.content(for: .ad), .podcastAd)
        XCTAssertEqual(context.content(for: .ad), .podcastAd)
        XCTAssertEqual(context.content(for: .song), .song)
        XCTAssertEqual(context.content(for: .ad), .musicAd)
    }

    func testArtworkCacheKeepsMostRecent() {
        var cache = ArtworkCache(capacity: 2)
        cache.insert(Data([1]), for: "a")
        cache.insert(Data([2]), for: "b")
        XCTAssertEqual(cache.image(for: "a"), Data([1]))
        cache.insert(Data([3]), for: "c")
        XCTAssertNil(cache.image(for: "b"))
        XCTAssertEqual(cache.urls, ["a", "c"])
        cache.insert(Data([4]), for: "a")
        XCTAssertEqual(cache.urls, ["c", "a"])
        XCTAssertEqual(cache.image(for: "a"), Data([4]))
    }
}
