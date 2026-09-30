import Foundation

/// Runs the AppleScript query. Returns the reply text, or nil if the query failed
/// (permission denied, timeout, Spotify gone).
public protocol SpotifyQueryRunning: Sendable {
    func run() async -> String?
}

/// What Spotify is doing right now, as far as the app knows.
public struct SpotifySnapshot: Sendable, Equatable {
    public var nowPlaying: NowPlaying
    public var content: SpotifyContent

    public init(nowPlaying: NowPlaying, content: SpotifyContent) {
        self.nowPlaying = nowPlaying
        self.content = content
    }
}

/// Keeps track of Spotify's running state and current item.
///
/// Sending an Apple Event to a closed app launches it, so the query only runs while
/// Spotify is known to be running (from launch/quit notifications), and a reply that
/// arrives after Spotify quit is thrown away. Refreshes that come in while a query is
/// running are merged into one follow-up query instead of piling up.
@MainActor
public final class SpotifyTracker {
    public private(set) var isRunning: Bool
    public private(set) var current: SpotifySnapshot?
    /// Called after every refresh and on quit, with the new snapshot (nil = no current track).
    public var onUpdate: ((SpotifySnapshot?) -> Void)?

    private let runner: any SpotifyQueryRunning
    private var adContext = AdContext()
    private var querying = false
    private var pending = false

    public init(runner: any SpotifyQueryRunning, isRunning: Bool) {
        self.runner = runner
        self.isRunning = isRunning
    }

    public func spotifyLaunched() {
        isRunning = true
    }

    public func spotifyTerminated() {
        isRunning = false
        pending = false
        adContext = AdContext()
        publish(nil)
    }

    /// Spotify said it stopped (its notification's player state). No query: a stopping
    /// Spotify may be quitting, and an Apple Event now could launch it again.
    public func spotifyStopped() {
        pending = false
        publish(nil)
    }

    /// Queries Spotify for its full state. Does nothing while Spotify isn't running.
    public func refresh() async {
        guard isRunning else { return }
        if querying {
            pending = true
            return
        }
        querying = true
        defer { querying = false }
        repeat {
            pending = false
            let reply = await runner.run()
            // Spotify may have quit while the query ran.
            guard isRunning else { return }
            publish(reply.flatMap(SpotifyQuery.parse).map { nowPlaying in
                SpotifySnapshot(nowPlaying: nowPlaying, content: adContext.content(for: nowPlaying.kind))
            })
        } while pending && isRunning
    }

    private func publish(_ snapshot: SpotifySnapshot?) {
        current = snapshot
        onUpdate?(snapshot)
    }
}
