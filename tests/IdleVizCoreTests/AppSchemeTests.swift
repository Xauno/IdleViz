import XCTest
@testable import IdleVizCore

final class AppSchemeTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appending(path: "AppSchemeTests-\(UUID().uuidString)/web")
        try FileManager.default.createDirectory(at: root.appending(path: "fonts"), withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root.deletingLastPathComponent())
    }

    private func url(_ string: String) throws -> URL { try XCTUnwrap(URL(string: string)) }

    func testResolvesFilesInsideTheRoot() throws {
        let base = root.standardizedFileURL.resolvingSymlinksInPath().path
        XCTAssertEqual(AppScheme.file(for: try url("idleviz-app://app/index.html"), in: root)?.path, base + "/index.html")
        XCTAssertEqual(
            AppScheme.file(for: try url("idleviz-app://app/fonts/Figtree%5Bwght%5D.ttf"), in: root)?.path,
            base + "/fonts/Figtree[wght].ttf"
        )
    }

    func testRejectsEscapes() throws {
        XCTAssertNil(AppScheme.file(for: try url("idleviz-app://app/../secret.txt"), in: root))
        XCTAssertNil(AppScheme.file(for: try url("idleviz-app://app/fonts/../../secret.txt"), in: root))
        XCTAssertNil(AppScheme.file(for: try url("idleviz-app://app/%2E%2E/secret.txt"), in: root))
        XCTAssertNil(AppScheme.file(for: try url("idleviz-app://app/"), in: root))
    }

    func testRejectsSymlinksOutOfTheRoot() throws {
        let link = root.appending(path: "escape")
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: URL(filePath: "/etc"))
        XCTAssertNil(AppScheme.file(for: try url("idleviz-app://app/escape/hosts"), in: root))
    }

    func testRejectsOtherHostsAndSchemes() throws {
        XCTAssertNil(AppScheme.file(for: try url("idleviz-app://presets/a.json"), in: root))
        XCTAssertNil(AppScheme.file(for: try url("idleviz://app/index.html"), in: root))
        XCTAssertNil(AppScheme.file(for: try url("file:///etc/hosts"), in: root))
    }

    func testMimeTypes() {
        XCTAssertEqual(AppScheme.mimeType(for: URL(filePath: "/a/overlay.js")), "text/javascript; charset=utf-8")
        XCTAssertEqual(AppScheme.mimeType(for: URL(filePath: "/a/index.html")), "text/html; charset=utf-8")
        XCTAssertEqual(AppScheme.mimeType(for: URL(filePath: "/a/Figtree[wght].ttf")), "font/ttf")
        XCTAssertEqual(AppScheme.mimeType(for: URL(filePath: "/a/unknown.bin")), "application/octet-stream")
    }

    func testCSPMatchesTheDesign() {
        XCTAssertTrue(AppScheme.contentSecurityPolicy.hasPrefix("default-src 'none'; "))
        XCTAssertTrue(AppScheme.contentSecurityPolicy.contains("img-src 'self' data: blob:"))
        XCTAssertFalse(AppScheme.contentSecurityPolicy.contains("http"))
    }
}

final class OverlayPayloadTests: XCTestCase {
    private func snapshot(name: String = "Riot", url: String = "spotify:track:a") -> SpotifySnapshot {
        let item = NowPlaying(
            state: .paused, name: name, artist: "Hollywood Undead", album: "Five", artworkURL: "https://i.scdn.co/x",
            durationMS: 228_000, position: 17.5, spotifyURL: url, kind: .song
        )
        return SpotifySnapshot(nowPlaying: item, content: .song)
    }

    private let jpeg = Data([0xFF, 0xD8, 0xFF, 0xE0, 0, 1])

    func testFields() {
        let payload = OverlayPayload(snapshot: snapshot(), artwork: jpeg, artworkPending: false)
        XCTAssertEqual(payload.id, "spotify:track:a")
        XCTAssertEqual(payload.state, "paused")
        XCTAssertEqual(payload.content, "song")
        XCTAssertEqual(payload.durationMs, 228_000)
        XCTAssertEqual(payload.position, 17.5)
        XCTAssertEqual(payload.artwork, "data:image/jpeg;base64,\(jpeg.base64EncodedString())")
        XCTAssertFalse(payload.artworkPending)
    }

    func testPendingOnlyWithoutArtwork() {
        XCTAssertTrue(OverlayPayload(snapshot: snapshot(), artwork: nil, artworkPending: true).artworkPending)
        XCTAssertFalse(OverlayPayload(snapshot: snapshot(), artwork: jpeg, artworkPending: true).artworkPending)
    }

    func testDataURLSniffsTheType() {
        XCTAssertEqual(OverlayPayload.dataURL(for: Data([0x89, 0x50, 0x4E, 0x47, 0]))?.hasPrefix("data:image/png;"), true)
        let webp = Data([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50])
        XCTAssertEqual(OverlayPayload.dataURL(for: webp)?.hasPrefix("data:image/webp;"), true)
        XCTAssertNil(OverlayPayload.dataURL(for: Data("<svg onload=alert(1)>".utf8)))
    }

    func testScriptEscapesTitles() throws {
        let tricky = "\"); alert(1); (\"\u{2028}</script>"
        let script = OverlayPayload.script(for: OverlayPayload(snapshot: snapshot(name: tricky), artwork: nil, artworkPending: false))
        XCTAssertTrue(script.hasPrefix("window.nowPlaying?.({"))
        let json = String(script.dropFirst("window.nowPlaying?.(".count).dropLast())
        let decoded = try JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any]
        XCTAssertEqual(decoded?["title"] as? String, tricky)
        XCTAssertFalse(json.contains("\u{2028}"))
    }

    func testNullWithoutATrack() {
        XCTAssertEqual(OverlayPayload.script(for: nil), "window.nowPlaying?.(null)")
    }
}
