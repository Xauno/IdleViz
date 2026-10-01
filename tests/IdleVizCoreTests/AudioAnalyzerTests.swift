import XCTest
@testable import IdleVizCore

final class AudioAnalyzerTests: XCTestCase {
    private let rate = 48_000.0
    private let count = AudioFrame.sampleCount

    private func sine(_ hertz: Double, amplitude: Float, offset: Int = 0) -> [Float] {
        (0..<count).map { amplitude * Float(sin(2 * Double.pi * hertz * Double($0 + offset) / rate)) }
    }

    /// Runs enough frames for the band smoothing and the gain to settle.
    private func settle(_ analyzer: inout AudioAnalyzer, left: [Float], right: [Float], frames: Int = 120) -> AudioFrame {
        var frame = analyzer.analyze(left: left, right: right, sampleRate: rate)
        for _ in 1..<frames { frame = analyzer.analyze(left: left, right: right, sampleRate: rate) }
        return frame
    }

    private func band(for hertz: Double) -> Int {
        Int(Double(AudioFrame.bandCount) * log(hertz / 40) / log(400))
    }

    func testSilenceStaysSilent() {
        var analyzer = AudioAnalyzer()
        let zeros = [Float](repeating: 0, count: count)
        let frame = settle(&analyzer, left: zeros, right: zeros)
        XCTAssertEqual(frame.bands, [Float](repeating: 0, count: 64))
        XCTAssertEqual([frame.bass, frame.mid, frame.treble, frame.rms], [0, 0, 0, 0])
        XCTAssertEqual(frame.monoBytes, [UInt8](repeating: 128, count: count))
        XCTAssertEqual(frame.leftBytes, frame.monoBytes)
        XCTAssertEqual(analyzer.gain, 1)
    }

    func testBandsFallBackToExactZeroAfterSound() {
        var analyzer = AudioAnalyzer()
        let tone = sine(1000, amplitude: 0.3)
        _ = settle(&analyzer, left: tone, right: tone)
        let zeros = [Float](repeating: 0, count: count)
        let frame = settle(&analyzer, left: zeros, right: zeros, frames: 240)
        XCTAssertEqual(frame.bands.max(), 0)
    }

    func testAToneLightsUpItsBand() {
        var analyzer = AudioAnalyzer()
        let tone = sine(1000, amplitude: 0.2)
        let frame = settle(&analyzer, left: tone, right: tone)
        let loudest = frame.bands.indices.max { frame.bands[$0] < frame.bands[$1] }
        XCTAssertEqual(loudest, band(for: 1000))
        XCTAssertGreaterThan(frame.bands[band(for: 1000)], 0.8)
        XCTAssertLessThan(frame.bands[band(for: 100)], 0.1)
        XCTAssertLessThan(frame.bands[band(for: 10_000)], 0.1)
        // Bands 30–63 start at about 660 Hz, so 1 kHz counts as treble.
        XCTAssertGreaterThan(frame.treble, frame.bass)
    }

    func testBassToneReadsAsBass() {
        var analyzer = AudioAnalyzer()
        let tone = sine(60, amplitude: 0.2)
        let frame = settle(&analyzer, left: tone, right: tone)
        XCTAssertGreaterThan(frame.bass, 0.3)
        XCTAssertGreaterThan(frame.bass, frame.mid * 2)
        XCTAssertLessThan(frame.treble, 0.05)
    }

    func testAFullScaleSineOnABinReadsOne() {
        // Bin 64 of 512 is 3 kHz at 48 kHz, so the sine fits the window exactly.
        let tone = (0..<count).map { Float(sin(2 * Double.pi * 64 * Double($0) / Double(count))) }
        let analyzer = AudioAnalyzer()
        XCTAssertEqual(AudioAnalyzer.magnitude(in: analyzer.spectrumForTesting(tone), from: 63.5, to: 64.5), 1, accuracy: 0.01)
    }

    func testBandValuesStayInRange() {
        var analyzer = AudioAnalyzer()
        var generator = SystemRandomNumberGenerator()
        for _ in 0..<60 {
            let left = (0..<count).map { _ in Float.random(in: -1...1, using: &generator) }
            let right = (0..<count).map { _ in Float.random(in: -1...1, using: &generator) }
            let frame = analyzer.analyze(left: left, right: right, sampleRate: rate)
            XCTAssertTrue(frame.bands.allSatisfy { $0 >= 0 && $0 <= 1 })
            XCTAssertTrue(frame.waveform.allSatisfy { $0 >= -1 && $0 <= 1 })
            XCTAssertTrue((0...1).contains(frame.rms))
        }
    }

    func testQuietAudioIsBroughtUp() {
        var loud = AudioAnalyzer()
        var quiet = AudioAnalyzer()
        // A minute, since the gain turns up slowly.
        let loudFrame = settle(&loud, left: sine(440, amplitude: 0.3), right: sine(440, amplitude: 0.3), frames: 3600)
        let quietFrame = settle(&quiet, left: sine(440, amplitude: 0.02), right: sine(440, amplitude: 0.02), frames: 3600)
        XCTAssertEqual(loudFrame.rms, AutoGain.target, accuracy: 0.02)
        XCTAssertEqual(quietFrame.rms, AutoGain.target, accuracy: 0.02)
        XCTAssertEqual(quietFrame.bands[band(for: 440)], loudFrame.bands[band(for: 440)], accuracy: 0.05)
    }

    func testChannelsKeepTheirOwnSamples() {
        var analyzer = AudioAnalyzer()
        let left = sine(440, amplitude: 0.28)
        let zeros = [Float](repeating: 0, count: count)
        let frame = analyzer.analyze(left: left, right: zeros, sampleRate: rate)
        XCTAssertEqual(frame.rightBytes, [UInt8](repeating: 128, count: count))
        XCTAssertNotEqual(frame.leftBytes, frame.rightBytes)
        XCTAssertGreaterThan(frame.leftBytes.max() ?? 0, 150)
    }

    func testBytesClampAtFullScale() {
        XCTAssertEqual(AudioAnalyzer.bytes([0, 1, -1, 0.5, 3, -3], gain: 1), [128, 255, 0, 192, 255, 0])
        XCTAssertEqual(AudioAnalyzer.bytes([0.25], gain: 2), [192])
    }

    func testSequenceCountsUp() {
        var analyzer = AudioAnalyzer()
        let zeros = [Float](repeating: 0, count: count)
        let sequences = (0..<3).map { _ in analyzer.analyze(left: zeros, right: zeros, sampleRate: rate).sequence }
        XCTAssertEqual(sequences, [0, 1, 2])
    }
}

final class AudioFrameTests: XCTestCase {
    func testPackedLayout() {
        var frame = AudioFrame.silence(sequence: 7, sampleRate: 44_100)
        frame.bass = 0.25
        frame.rms = 0.5
        frame.bands[63] = 1
        frame.waveform[0] = -1
        frame.rightBytes[1023] = 200
        let data = frame.packed()
        XCTAssertEqual(data.count, AudioFrame.byteLength)
        XCTAssertEqual(data.count, 7448)

        func float(at offset: Int) -> Float {
            Float(bitPattern: data.subdata(in: offset..<offset + 4).withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) })
        }
        XCTAssertEqual(data.prefix(4).withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) }, 7)
        XCTAssertEqual(float(at: 4), 44_100)
        XCTAssertEqual(float(at: 8), 0.25)
        XCTAssertEqual(float(at: 20), 0.5)
        XCTAssertEqual(float(at: 24 + 63 * 4), 1)
        XCTAssertEqual(float(at: 280), -1)
        XCTAssertEqual(data[4376], 128)
        XCTAssertEqual(data[7447], 200)
    }

    func testScriptIsASafeCall() {
        let script = AudioFrame.script(for: Data([0xFF, 0xFE, 0x27, 0x5C]))
        XCTAssertEqual(script, "window.audioFrame?.('//4nXA==')")
    }
}

final class AutoGainTests: XCTestCase {
    func testHoldsTheGainThroughSilence() {
        var gain = AutoGain()
        for _ in 0..<3600 { _ = gain.update(rms: 0.05, seconds: 1 / 60) }
        let settled = gain.gain
        XCTAssertEqual(settled, 4, accuracy: 0.1)
        for _ in 0..<600 { XCTAssertEqual(gain.update(rms: 0, seconds: 1 / 60), settled) }
        XCTAssertEqual(gain.update(rms: 0.0005, seconds: 1 / 60), settled)
    }

    func testStaysWithinLimits() {
        var quiet = AutoGain()
        for _ in 0..<6000 { _ = quiet.update(rms: 0.002, seconds: 1 / 60) }
        XCTAssertEqual(quiet.gain, AutoGain.maxGain)
        var loud = AutoGain()
        for _ in 0..<600 { _ = loud.update(rms: 0.9, seconds: 1 / 60) }
        XCTAssertEqual(loud.gain, AutoGain.minGain)
    }

    func testStartsAtUnityAndDoesNotJumpOnAFadeIn() {
        var gain = AutoGain()
        XCTAssertEqual(gain.gain, 1)
        // The first frames after pressing play are a quiet fade-in.
        for _ in 0..<6 { _ = gain.update(rms: 0.01, seconds: 1 / 60) }
        XCTAssertLessThan(gain.gain, 1.05)
    }

    func testTurnsDownQuicklyAndUpSlowly() {
        var gain = AutoGain()
        for _ in 0..<3600 { _ = gain.update(rms: 0.05, seconds: 1 / 60) }
        // One second of loud audio is enough to come most of the way down.
        for _ in 0..<60 { _ = gain.update(rms: 0.4, seconds: 1 / 60) }
        XCTAssertLessThan(gain.gain, 0.7)
        // One second of quiet audio barely raises it again.
        for _ in 0..<60 { _ = gain.update(rms: 0.05, seconds: 1 / 60) }
        XCTAssertLessThan(gain.gain, 0.8)
    }

    func testIgnoresBadInput() {
        var gain = AutoGain()
        XCTAssertEqual(gain.update(rms: .nan, seconds: 1 / 60), 1)
        XCTAssertEqual(gain.update(rms: .infinity, seconds: 1 / 60), 1)
    }
}

final class SampleRingTests: XCTestCase {
    private func latest(_ ring: SampleRing, _ count: Int) -> (left: [Float], right: [Float]) {
        var left = [Float](repeating: -9, count: count)
        var right = [Float](repeating: -9, count: count)
        ring.latest(left: &left, right: &right)
        return (left, right)
    }

    func testReturnsTheNewestSamplesOldestFirst() {
        let ring = SampleRing()
        ring.append(interleaved: [1, -1, 2, -2, 3, -3], channels: 2, frames: 3)
        let newest = latest(ring, 2)
        XCTAssertEqual(newest.left, [2, 3])
        XCTAssertEqual(newest.right, [-2, -3])
        XCTAssertEqual(latest(ring, 4).left, [0, 1, 2, 3])
    }

    func testWrapsAround() {
        let ring = SampleRing()
        let samples = (0..<SampleRing.capacity + 10).map(Float.init)
        ring.append(left: samples, right: samples, frames: samples.count)
        XCTAssertEqual(latest(ring, 3).left, [4103, 4104, 4105])
    }

    func testMonoGoesToBothSides() {
        let ring = SampleRing()
        ring.append(interleaved: [0.5, 0.25], channels: 1, frames: 2)
        let newest = latest(ring, 2)
        XCTAssertEqual(newest.left, [0.5, 0.25])
        XCTAssertEqual(newest.right, [0.5, 0.25])
    }

    func testCountsBuffersAndZeroBuffers() {
        let ring = SampleRing()
        ring.append(interleaved: [0, 0, 0, 0], channels: 2, frames: 2)
        ring.append(interleaved: [0, 0.1], channels: 2, frames: 1)
        ring.append(left: [0, 0], right: [0, 0], frames: 2)
        XCTAssertEqual(ring.takeCounts().buffers, 3)
        ring.append(left: [0], right: [0], frames: 1)
        let counts = ring.takeCounts()
        XCTAssertEqual(counts.buffers, 1)
        XCTAssertEqual(counts.zero, 1)
    }

    func testClearLeavesSilence() {
        let ring = SampleRing()
        ring.append(interleaved: [1, 1, 1, 1], channels: 2, frames: 2)
        ring.clear()
        XCTAssertEqual(latest(ring, 4).left, [0, 0, 0, 0])
    }
}

final class TapHealthTests: XCTestCase {
    func testSuspectsAfterFiveSilentSecondsWhilePlaying() {
        var health = TapHealth()
        for _ in 0..<4 { XCTAssertFalse(health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: true)) }
        XCTAssertTrue(health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: true))
        XCTAssertTrue(health.suspected)
        // Reported once, not every second.
        XCTAssertFalse(health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: true))
    }

    func testNoBuffersAtAllCountsToo() {
        var health = TapHealth()
        let results = (0..<5).map { _ in health.record(buffers: 0, zeroBuffers: 0, spotifyPlaying: true) }
        XCTAssertEqual(results, [false, false, false, false, true])
    }

    func testPausedTimeDoesNotCount() {
        var health = TapHealth()
        for _ in 0..<20 { XCTAssertFalse(health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: false)) }
        for _ in 0..<4 { _ = health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: true) }
        XCTAssertFalse(health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: false))
        XCTAssertFalse(health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: true))
    }

    func testRealAudioClearsTheSuspicion() {
        var health = TapHealth()
        for _ in 0..<5 { _ = health.record(buffers: 90, zeroBuffers: 90, spotifyPlaying: true) }
        XCTAssertFalse(health.record(buffers: 90, zeroBuffers: 12, spotifyPlaying: true))
        XCTAssertFalse(health.suspected)
    }
}

final class PageStatusTests: XCTestCase {
    func testReadsAStatusReply() {
        let reply: [String: Any] = ["preset": "bundled:Geiss - Swirl", "frames": 1200.0, "audioFrames": 1190.0, "failed": ["bundled:Bad"]]
        XCTAssertEqual(
            PageStatus(reply: reply),
            PageStatus(preset: "bundled:Geiss - Swirl", frames: 1200, audioFrames: 1190, failed: ["bundled:Bad"])
        )
    }

    func testRejectsAnythingThatIsNotAnObject() {
        XCTAssertNil(PageStatus(reply: nil))
        XCTAssertNil(PageStatus(reply: "ok"))
        XCTAssertNil(PageStatus(reply: [1, 2]))
    }

    func testTreatsTheReplyAsUntrusted() {
        let long = String(repeating: "x", count: 5000)
        let reply: [String: Any] = [
            "preset": long,
            "frames": Double.infinity,
            "audioFrames": -5.0,
            "failed": [long, 3, ["nested"]] + [Any](repeating: "f", count: 500),
        ]
        let status = PageStatus(reply: reply)
        XCTAssertEqual(status?.preset?.count, PageStatus.maxTextLength)
        XCTAssertEqual(status?.frames, 0)
        XCTAssertEqual(status?.audioFrames, 0)
        XCTAssertEqual(status?.failed.first?.count, PageStatus.maxTextLength)
        XCTAssertLessThanOrEqual(status?.failed.count ?? .max, PageStatus.maxFailures)
    }

    func testMissingFieldsReadAsEmpty() {
        XCTAssertEqual(PageStatus(reply: [String: Any]()), PageStatus(preset: nil, frames: 0, audioFrames: 0, failed: []))
    }
}

final class PageWatchdogTests: XCTestCase {
    func testReloadsAfterThreeSecondsWithoutAReply() {
        var watchdog = PageWatchdog()
        watchdog.start(at: 100)
        XCTAssertFalse(watchdog.shouldReload(at: 101))
        XCTAssertFalse(watchdog.shouldReload(at: 102))
        XCTAssertTrue(watchdog.shouldReload(at: 103))
        // The reloaded page gets a fresh three seconds.
        XCTAssertFalse(watchdog.shouldReload(at: 104))
        XCTAssertTrue(watchdog.shouldReload(at: 106))
    }

    func testRepliesKeepItQuiet() {
        var watchdog = PageWatchdog()
        watchdog.start(at: 0)
        for second in 1...20 {
            XCTAssertFalse(watchdog.shouldReload(at: TimeInterval(second)))
            watchdog.replied(at: TimeInterval(second) + 0.01)
        }
    }

    func testDoesNothingWhileStopped() {
        var watchdog = PageWatchdog()
        XCTAssertFalse(watchdog.shouldReload(at: 50))
        watchdog.start(at: 0)
        watchdog.stop()
        watchdog.replied(at: 1)
        XCTAssertFalse(watchdog.shouldReload(at: 50))
    }
}
