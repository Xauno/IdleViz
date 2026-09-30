import AppKit
import IdleVizCore
import os

/// Reads what Spotify is playing, without ever launching it.
///
/// Launch/quit notifications keep the running state, Spotify's own playback notification
/// triggers one AppleScript query for the full state, and artwork is downloaded here so
/// the page never touches the network. Each change goes to the overlay through `onOverlay`.
@MainActor
final class SpotifyInfo {
    nonisolated static let bundleID = "com.spotify.client"

    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "spotify")
    private let tracker: SpotifyTracker
    private var artwork = ArtworkCache(capacity: 5)
    private var artworkTask: Task<Void, Never>?
    private var artworkURL: String?
    private var resync: Task<Void, Never>?
    private var observers: [(NotificationCenter, NSObjectProtocol)] = []

    /// Called with what the overlay should show: on every snapshot, and again when its artwork arrives.
    var onOverlay: ((OverlayPayload?) -> Void)?

    var current: SpotifySnapshot? { tracker.current }
    var isRunning: Bool { tracker.isRunning }

    init() {
        tracker = SpotifyTracker(runner: AppleScriptRunner(), isRunning: Self.spotifyIsRunning())
        tracker.onUpdate = { [weak self] snapshot in self?.didUpdate(snapshot) }
        observe()
        log.notice("Spotify is \(self.tracker.isRunning ? "running" : "not running", privacy: .public)")
        refresh()
    }

    /// While the visualizer is open, re-query every few seconds to correct progress drift and catch seeks.
    func startResync(every interval: Duration = .seconds(5)) {
        refresh()
        resync?.cancel()
        resync = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: interval)
                guard !Task.isCancelled else { return }
                self?.refresh()
            }
        }
    }

    /// Why the visualizer may not open right now, or nil if it may.
    func openRefusal() async -> OpenRefusal? {
        let snapshot = await tracker.knownSnapshot()
        return OpenRules.refusal(spotifyRunning: tracker.isRunning, snapshot: snapshot)
    }

    func stopResync() {
        resync?.cancel()
        resync = nil
    }

    func refresh() {
        Task { await tracker.refresh() }
    }

    private func observe() {
        let workspace = NSWorkspace.shared.notificationCenter
        observers.append((workspace, workspace.addObserver(
            forName: NSWorkspace.didLaunchApplicationNotification, object: nil, queue: .main
        ) { [weak self] note in
            guard Self.isSpotify(note) else { return }
            MainActor.assumeIsolated {
                self?.log.notice("Spotify launched")
                self?.tracker.spotifyLaunched()
                self?.refresh()
            }
        }))
        observers.append((workspace, workspace.addObserver(
            forName: NSWorkspace.didTerminateApplicationNotification, object: nil, queue: .main
        ) { [weak self] note in
            guard Self.isSpotify(note) else { return }
            MainActor.assumeIsolated {
                self?.log.notice("Spotify quit")
                self?.tracker.spotifyTerminated()
            }
        }))

        let distributed = DistributedNotificationCenter.default()
        observers.append((distributed, distributed.addObserver(
            forName: Notification.Name("com.spotify.client.PlaybackStateChanged"), object: nil, queue: .main
        ) { [weak self] note in
            let state = note.userInfo?["Player State"] as? String
            MainActor.assumeIsolated {
                if state == "Stopped" {
                    self?.tracker.spotifyStopped()
                } else {
                    self?.refresh()
                }
            }
        }))
    }

    private func didUpdate(_ snapshot: SpotifySnapshot?) {
        guard let snapshot else {
            log.notice("Now playing: nothing")
            loadArtwork(for: nil)
            onOverlay?(nil)
            return
        }
        let item = snapshot.nowPlaying
        let position = Int(item.position.rounded())
        let duration = item.durationMS / 1000
        log.notice("""
            Now playing: \(snapshot.content.rawValue, privacy: .public), \(item.state.rawValue, privacy: .public), \
            "\(item.name, privacy: .public)" by \(item.artist, privacy: .public) \
            from \(item.album, privacy: .public), \(position, privacy: .public)/\(duration, privacy: .public) s, \
            \(item.spotifyURL, privacy: .public)
            """)
        loadArtwork(for: item.artworkURL.isEmpty ? nil : item.artworkURL)
        publishOverlay()
    }

    private func publishOverlay() {
        guard let snapshot = tracker.current else { return }
        let url = snapshot.nowPlaying.artworkURL
        let image = url.isEmpty ? nil : artwork.image(for: url)
        onOverlay?(OverlayPayload(snapshot: snapshot, artwork: image, artworkPending: artworkTask != nil && image == nil))
    }

    private func loadArtwork(for url: String?) {
        guard url != artworkURL else { return }
        artworkURL = url
        artworkTask?.cancel()
        artworkTask = nil
        guard let url else { return }
        if let data = artwork.image(for: url) {
            log.notice("Artwork: \(data.count / 1024, privacy: .public) KB (cached)")
            return
        }
        guard let remote = URL(string: url), remote.scheme == "https" else {
            log.error("Artwork: unusable URL \(url, privacy: .public)")
            return
        }
        artworkTask = Task { [weak self] in
            do {
                let (data, response) = try await URLSession.shared.data(from: remote)
                guard let self, !Task.isCancelled else { return }
                self.artworkTask = nil
                if (response as? HTTPURLResponse)?.statusCode == 200, NSImage(data: data) != nil {
                    self.artwork.insert(data, for: url)
                    self.log.notice("Artwork: \(data.count / 1024, privacy: .public) KB (downloaded)")
                } else {
                    self.log.error("Artwork: bad response for \(url, privacy: .public)")
                }
                self.publishOverlay()
            } catch {
                guard let self, !Task.isCancelled else { return }
                self.log.error("Artwork: \(error.localizedDescription, privacy: .public)")
                self.artworkTask = nil
                self.publishOverlay()
            }
        }
    }

    private nonisolated static func isSpotify(_ note: Notification) -> Bool {
        let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
        return app?.bundleIdentifier == bundleID
    }

    private static func spotifyIsRunning() -> Bool {
        NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).contains { !$0.isTerminated }
    }
}

/// Runs the AppleScript query on one dedicated thread. `NSAppleScript` isn't thread-safe,
/// and a hung Spotify must never block the main thread; the script's own 2 s timeout bounds each call.
final class AppleScriptRunner: SpotifyQueryRunning, @unchecked Sendable {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "spotify")
    private let condition = NSCondition()
    private var jobs: [() -> Void] = []
    // Only touched on the script thread.
    private var script: NSAppleScript?

    init() {
        let thread = Thread { [weak self] in self?.loop() }
        thread.name = "IdleViz AppleScript"
        thread.qualityOfService = .utility
        thread.start()
    }

    func run() async -> String? {
        await withCheckedContinuation { continuation in
            enqueue { continuation.resume(returning: self.execute()) }
        }
    }

    private func enqueue(_ job: @escaping () -> Void) {
        condition.lock()
        jobs.append(job)
        condition.signal()
        condition.unlock()
    }

    private func loop() {
        while true {
            condition.lock()
            while jobs.isEmpty { condition.wait() }
            let job = jobs.removeFirst()
            condition.unlock()
            job()
        }
    }

    private func execute() -> String? {
        // Last check right before sending: an Apple Event to a closed Spotify would launch it.
        let running = NSRunningApplication.runningApplications(withBundleIdentifier: SpotifyInfo.bundleID)
        guard running.contains(where: { !$0.isTerminated }) else { return nil }

        if script == nil {
            let compiled = NSAppleScript(source: SpotifyQuery.source)
            var error: NSDictionary?
            if compiled?.compileAndReturnError(&error) != true {
                log.error("AppleScript didn't compile: \(String(describing: error), privacy: .public)")
                return nil
            }
            script = compiled
        }
        var error: NSDictionary?
        let result = script?.executeAndReturnError(&error)
        if let error {
            let number = error[NSAppleScript.errorNumber] as? Int ?? 0
            let message = error[NSAppleScript.errorMessage] as? String ?? ""
            // -1743: Automation permission denied. -1712: Spotify didn't answer within the timeout.
            log.error("AppleScript error \(number, privacy: .public): \(message, privacy: .public)")
            return nil
        }
        return result?.stringValue
    }
}
