import XCTest
@testable import IdleVizCore

/// Hands out queued replies. `hold()` makes the next run wait until `release()`.
private final class FakeRunner: SpotifyQueryRunning, @unchecked Sendable {
    private let lock = NSLock()
    private var replies: [String?]
    private var gate: CheckedContinuation<Void, Never>?
    private var holdNext = false
    private(set) var runs = 0

    init(_ replies: [String?]) {
        self.replies = replies
    }

    func hold() {
        lock.withLock { holdNext = true }
    }

    func release() {
        let gate = lock.withLock { () -> CheckedContinuation<Void, Never>? in
            defer { self.gate = nil }
            return self.gate
        }
        gate?.resume()
    }

    var isWaiting: Bool { lock.withLock { gate != nil } }

    func run() async -> String? {
        let shouldHold = lock.withLock { () -> Bool in
            runs += 1
            defer { holdNext = false }
            return holdNext
        }
        if shouldHold {
            await withCheckedContinuation { continuation in lock.withLock { gate = continuation } }
        }
        return lock.withLock { replies.isEmpty ? nil : replies.removeFirst() }
    }
}

@MainActor
final class SpotifyTrackerTests: XCTestCase {
    private func reply(_ state: String, _ url: String, name: String = "Song") -> String {
        [state, name, "Artist", "Album", "https://i.scdn.co/image/x", "200000", "1.0", url]
            .joined(separator: String(SpotifyQuery.separator))
    }

    private func waitUntil(_ condition: () -> Bool) async {
        for _ in 0..<200 where !condition() {
            await Task.yield()
            try? await Task.sleep(for: .milliseconds(1))
        }
    }

    func testNeverQueriesWhileNotRunning() async {
        let runner = FakeRunner([reply("playing", "spotify:track:a")])
        let tracker = SpotifyTracker(runner: runner, isRunning: false)
        await tracker.refresh()
        XCTAssertEqual(runner.runs, 0)
        XCTAssertNil(tracker.current)
    }

    func testRefreshReadsSnapshot() async {
        let runner = FakeRunner([reply("playing", "spotify:track:a")])
        let tracker = SpotifyTracker(runner: runner, isRunning: true)
        var updates: [SpotifySnapshot?] = []
        tracker.onUpdate = { updates.append($0) }
        await tracker.refresh()
        XCTAssertEqual(runner.runs, 1)
        XCTAssertEqual(tracker.current?.content, .song)
        XCTAssertEqual(updates.count, 1)
    }

    func testFailedQueryMeansNoTrack() async {
        let runner = FakeRunner([nil])
        let tracker = SpotifyTracker(runner: runner, isRunning: true)
        await tracker.refresh()
        XCTAssertNil(tracker.current)
    }

    func testLaunchAndQuit() async {
        let runner = FakeRunner([reply("playing", "spotify:track:a")])
        let tracker = SpotifyTracker(runner: runner, isRunning: false)
        tracker.spotifyLaunched()
        await tracker.refresh()
        XCTAssertNotNil(tracker.current)
        tracker.spotifyTerminated()
        XCTAssertFalse(tracker.isRunning)
        XCTAssertNil(tracker.current)
        await tracker.refresh()
        XCTAssertEqual(runner.runs, 1)
    }

    func testStoppedClearsWithoutQuerying() async {
        let runner = FakeRunner([reply("playing", "spotify:track:a")])
        let tracker = SpotifyTracker(runner: runner, isRunning: true)
        await tracker.refresh()
        tracker.spotifyStopped()
        XCTAssertNil(tracker.current)
        XCTAssertEqual(runner.runs, 1)
    }

    func testReplyAfterQuitIsDropped() async {
        let runner = FakeRunner([reply("playing", "spotify:track:a")])
        runner.hold()
        let tracker = SpotifyTracker(runner: runner, isRunning: true)
        let refresh = Task { await tracker.refresh() }
        await waitUntil { runner.isWaiting }
        tracker.spotifyTerminated()
        runner.release()
        await refresh.value
        XCTAssertNil(tracker.current)
    }

    func testRefreshesDuringAQueryMergeIntoOne() async {
        let runner = FakeRunner([reply("playing", "spotify:track:a"), reply("paused", "spotify:track:b")])
        runner.hold()
        let tracker = SpotifyTracker(runner: runner, isRunning: true)
        let first = Task { await tracker.refresh() }
        await waitUntil { runner.isWaiting }
        await tracker.refresh()
        await tracker.refresh()
        await tracker.refresh()
        runner.release()
        await first.value
        XCTAssertEqual(runner.runs, 2)
        XCTAssertEqual(tracker.current?.nowPlaying.spotifyURL, "spotify:track:b")
        XCTAssertEqual(tracker.current?.nowPlaying.state, .paused)
    }

    func testAdsAfterPodcastAreSplitFromMusicAds() async {
        let runner = FakeRunner([
            reply("playing", "spotify:episode:e"),
            reply("playing", "spotify:ad:x", name: ""),
            reply("playing", "spotify:track:a"),
            reply("playing", "spotify:ad:y", name: ""),
        ])
        let tracker = SpotifyTracker(runner: runner, isRunning: true)
        var contents: [SpotifyContent?] = []
        tracker.onUpdate = { contents.append($0?.content) }
        for _ in 0..<4 { await tracker.refresh() }
        XCTAssertEqual(contents, [.podcast, .podcastAd, .song, .musicAd])
    }

    func testQuitForgetsAdContext() async {
        let runner = FakeRunner([reply("playing", "spotify:episode:e"), reply("playing", "spotify:ad:x", name: "")])
        let tracker = SpotifyTracker(runner: runner, isRunning: true)
        await tracker.refresh()
        tracker.spotifyTerminated()
        tracker.spotifyLaunched()
        await tracker.refresh()
        XCTAssertEqual(tracker.current?.content, .musicAd)
    }
}
