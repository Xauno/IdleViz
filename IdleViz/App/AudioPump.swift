import AVFAudio
import Foundation
import IdleVizCore
import os

/// While the window is open: taps Spotify, analyses the newest samples 60 times a second and
/// hands each packed frame to the page.
@MainActor
final class AudioPump {
    static let framesPerSecond = 60.0

    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "audio")
    private let tap = SpotifyAudioTap()
    private var analyzer = AudioAnalyzer()
    private var health = TapHealth()
    private var timer: Timer?
    private var left = [Float](repeating: 0, count: AudioFrame.sampleCount)
    private var right = [Float](repeating: 0, count: AudioFrame.sampleCount)
    private var lastFrame: ContinuousClock.Instant?
    private var lastHealthCheck = ContinuousClock.now
    private var lastLevels = (rms: Float(0), bass: Float(0))
    private var delayLine = DelayLine<Data>()

    /// How long each frame waits before it goes to the page, so the visuals match what the speakers play.
    var delay: TimeInterval = 0

    /// Receives each packed frame.
    var onFrame: ((Data) -> Void)?
    /// Whether Spotify says it's playing, for telling a paused Spotify from a tap without permission.
    var spotifyIsPlaying: () -> Bool = { false }

    func start() {
        guard timer == nil else { return }
        tap.retain()
        // Frames from the last time the window was open are stale; start from silence.
        delayLine.removeAll()
        onFrame?(AudioFrame.silence(sequence: 0, sampleRate: Float(tap.sampleRate)).packed())
        _ = tap.ring.takeCounts()
        health = TapHealth()
        lastFrame = nil
        lastHealthCheck = .now
        let timer = Timer(timeInterval: 1 / Self.framesPerSecond, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        // Common modes, so frames keep flowing while a menu or the settings window tracks the mouse.
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        guard let timer else { return }
        timer.invalidate()
        self.timer = nil
        tap.release()
    }

    // MARK: Detect delay

    /// Starts keeping the tap's signal for Detect delay. The tap runs for this even while the window is closed.
    func startRecording(seconds: Double) {
        tap.retain()
        tap.ring.startRecording(maxSamples: Int(seconds * tap.sampleRate))
    }

    /// Stops and returns what the tap delivered, with the time it handed over the first sample in host-clock seconds.
    func stopRecording() -> DelayDetector.Recording {
        let (samples, startHostTime) = tap.ring.stopRecording()
        let sampleRate = tap.sampleRate
        tap.release()
        return DelayDetector.Recording(samples: samples, sampleRate: sampleRate, start: AVAudioTime.seconds(forHostTime: startHostTime))
    }

    private func tick() {
        let now = ContinuousClock.now
        let seconds = lastFrame.map { Float(($0.duration(to: now)) / .seconds(1)) } ?? Float(1 / Self.framesPerSecond)
        lastFrame = now
        tap.ring.latest(left: &left, right: &right)
        let frame = analyzer.analyze(left: left, right: right, sampleRate: tap.sampleRate, seconds: min(seconds, 0.25))
        lastLevels = (frame.rms, frame.bass)
        let uptime = ProcessInfo.processInfo.systemUptime
        delayLine.push(frame.packed(), at: uptime)
        // Until a frame is old enough, the page keeps showing the last one it got.
        if let due = delayLine.pop(at: uptime, delay: delay) { onFrame?(due) }
        if lastHealthCheck.duration(to: now) >= .seconds(1) {
            lastHealthCheck = now
            checkHealth()
        }
    }

    private func checkHealth() {
        let counts = tap.ring.takeCounts()
        // Only visible with `log stream --level debug`.
        log.debug("""
            Tap: \(counts.buffers, privacy: .public) buffers (\(counts.zero, privacy: .public) silent), \
            gain \(self.analyzer.gain, privacy: .public), rms \(self.lastLevels.rms, privacy: .public), \
            bass \(self.lastLevels.bass, privacy: .public)
            """)
        guard tap.isTapping else { return }
        if health.record(buffers: counts.buffers, zeroBuffers: counts.zero, spotifyPlaying: spotifyIsPlaying()) {
            log.warning("""
                Spotify is playing but the tap has heard only silence for \(TapHealth.suspectAfterSeconds, privacy: .public) s. \
                System Audio Recording is probably not allowed for IdleViz \
                (or Spotify is playing on another device, or is muted).
                """)
        }
    }
}
