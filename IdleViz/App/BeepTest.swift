import AVFoundation
import IdleVizCore
import os

/// Plays the manual delay test's beeps on the default output device, each at an exact time on
/// the host clock, so the settings sheet can light its panel one audio delay later.
@MainActor
final class BeepTestPlayer {
    /// The first beep comes this long after the test starts.
    private static let leadSeconds = 0.5
    /// Beeps are handed to the player this far ahead.
    private static let scheduleAheadSeconds = 2.0

    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "delay")
    private var engine: AVAudioEngine?
    private var player: AVAudioPlayerNode?
    private var beep: AVAudioPCMBuffer?
    private var accentBeep: AVAudioPCMBuffer?
    private var scheduler: Task<Void, Never>?
    private var configurationObserver: NSObjectProtocol?
    private var scheduled = 0
    /// When the first beep is sent to the speakers, on the host clock (`now`). Nil while stopped.
    private(set) var firstBeep: TimeInterval?

    /// Seconds on the host clock, the one the beeps are scheduled on.
    static var now: TimeInterval { AVAudioTime.seconds(forHostTime: mach_absolute_time()) }

    func start() throws {
        stop()
        let engine = AVAudioEngine()
        let player = AVAudioPlayerNode()
        let sampleRate = engine.outputNode.outputFormat(forBus: 0).sampleRate
        guard sampleRate > 0, let format = AVAudioFormat(standardFormatWithSampleRate: sampleRate, channels: 1),
              let beep = Self.buffer(BeepTest.samples(accent: false, sampleRate: sampleRate), format: format),
              let accentBeep = Self.buffer(BeepTest.samples(accent: true, sampleRate: sampleRate), format: format)
        else {
            throw NSError(domain: "IdleViz", code: 2, userInfo: [NSLocalizedDescriptionKey: "No speakers or headphones found"])
        }
        engine.attach(player)
        engine.connect(player, to: engine.mainMixerNode, format: format)
        try engine.start()
        player.play()
        self.engine = engine
        self.player = player
        self.beep = beep
        self.accentBeep = accentBeep
        scheduled = 0
        firstBeep = Self.now + Self.leadSeconds

        // Switching speakers or headphones stops the engine. Start over on the new device.
        configurationObserver = NotificationCenter.default.addObserver(
            forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.restart() }
        }
        scheduler = Task { [weak self] in
            while !Task.isCancelled {
                self?.scheduleAhead()
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
    }

    func stop() {
        scheduler?.cancel()
        scheduler = nil
        if let configurationObserver { NotificationCenter.default.removeObserver(configurationObserver) }
        configurationObserver = nil
        player?.stop()
        engine?.stop()
        player = nil
        engine = nil
        firstBeep = nil
    }

    private func restart() {
        do {
            try start()
        } catch {
            log.error("Couldn't restart the test beeps: \(error.localizedDescription, privacy: .public)")
            stop()
        }
    }

    private func scheduleAhead() {
        guard let player, let beep, let accentBeep, let firstBeep else { return }
        let until = Self.now + Self.scheduleAheadSeconds
        while BeepTest.beepTime(scheduled, start: firstBeep) < until {
            let time = AVAudioTime(hostTime: AVAudioTime.hostTime(forSeconds: BeepTest.beepTime(scheduled, start: firstBeep)))
            player.scheduleBuffer(BeepTest.isAccent(scheduled) ? accentBeep : beep, at: time)
            scheduled += 1
        }
    }

    private static func buffer(_ samples: [Float], format: AVAudioFormat) -> AVAudioPCMBuffer? {
        guard !samples.isEmpty,
              let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(samples.count)),
              let channel = buffer.floatChannelData?[0]
        else { return nil }
        samples.withUnsafeBufferPointer { channel.update(from: $0.baseAddress!, count: samples.count) }
        buffer.frameLength = AVAudioFrameCount(samples.count)
        return buffer
    }
}
