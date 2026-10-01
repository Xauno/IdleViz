import Accelerate
import Foundation

/// The Audio delay setting: how long the visuals wait so they match what the speakers play.
/// It is saved per output device, since built-in speakers, Bluetooth and AirPlay differ a lot.
public enum AudioDelaySetting {
    /// A dictionary from the output device's UID to its delay in seconds.
    public static let key = "audioDelayByDevice"
    public static let range = 0.0...2.5
    public static let step = 0.01

    /// A delay inside the slider's range, on one of its 10 ms steps.
    public static func normalized(_ seconds: Double) -> Double {
        guard seconds.isFinite else { return 0 }
        let clamped = min(max(seconds, range.lowerBound), range.upperBound)
        return (clamped / step).rounded() * step
    }

    /// The delay to use for a device: the saved one, or for a device seen for the first time the
    /// latency macOS reports for it. That is about right for AirPlay and often a little low for
    /// Bluetooth, so it's a starting point for Detect delay, not a replacement.
    public static func delay(forDevice uid: String, saved: [String: Double], reportedLatency: Double) -> Double {
        normalized(saved[uid] ?? reportedLatency)
    }

    /// Reads the saved delays, dropping anything that isn't a number.
    public static func saved(in defaults: UserDefaults) -> [String: Double] {
        (defaults.dictionary(forKey: key) ?? [:]).compactMapValues { ($0 as? NSNumber)?.doubleValue }
    }

    public static func label(_ seconds: Double) -> String {
        "\(Int((normalized(seconds) * 1000).rounded())) ms"
    }

    /// The call that tells the page the delay, so the progress bar shows the position you hear.
    public static func script(for seconds: Double) -> String {
        "window.setAudioDelay?.(\(normalized(seconds)))"
    }
}

/// Holds analysed frames and releases each one `delay` seconds after it was captured.
public struct DelayLine<Element> {
    /// More than the longest delay at 60 frames a second.
    public static var capacity: Int { 240 }

    private var items: [(time: TimeInterval, element: Element)] = []

    public init() {}

    public mutating func push(_ element: Element, at time: TimeInterval) {
        items.append((time, element))
        if items.count > Self.capacity { items.removeFirst(items.count - Self.capacity) }
    }

    /// The newest element captured at or before `time - delay`, or nil if none has come due since
    /// the last call. Elements older than the one returned are dropped.
    public mutating func pop(at time: TimeInterval, delay: TimeInterval) -> Element? {
        let due = time - delay
        guard let last = items.lastIndex(where: { $0.time <= due }) else { return nil }
        let element = items[last].element
        items.removeFirst(last + 1)
        return element
    }

    public mutating func removeAll() {
        items.removeAll()
    }

    public var count: Int { items.count }
}

/// Measures how far the sound from the speakers lags behind the audio Spotify sent, from two
/// recordings of the same few seconds: the tap's signal and the microphone's.
public enum DelayDetector {
    /// Loudness envelopes are sampled this many times a second, so lags are in milliseconds.
    public static let envelopeRate = 1000.0
    /// The peak has to stand this many standard deviations above the other lags…
    static let minimumZScore: Float = 6
    /// …and this far above the best lag elsewhere, or a steady beat could match one beat late.
    static let minimumPeakRatio: Float = 1.15
    /// Lags this close to the peak belong to it.
    static let peakWidth = 30

    public struct Estimate: Sendable, Equatable {
        /// Seconds the heard recording lags behind the sent one.
        public var delay: Double
        /// How far the peak stands above the other lags, in standard deviations.
        public var clarity: Double
    }

    /// Onset strength: how much the loudness rises in each millisecond. Beats and note starts
    /// stand out, and the tone of the speakers and the room matters little.
    public static func envelope(_ samples: [Float], sampleRate: Double) -> [Float] {
        let hop = sampleRate / envelopeRate
        let count = Int(Double(samples.count) / hop)
        guard count > 2, hop >= 1 else { return [] }
        var loudness = [Float](repeating: 0, count: count)
        samples.withUnsafeBufferPointer { pointer in
            for index in 0..<count {
                let start = Int(Double(index) * hop)
                let end = min(Int(Double(index + 1) * hop), samples.count)
                var meanSquare: Float = 0
                vDSP_measqv(pointer.baseAddress! + start, 1, &meanSquare, vDSP_Length(end - start))
                // Decibels, so a quiet recording and a loud one give the same shape.
                loudness[index] = 10 * log10(meanSquare + 1e-9)
            }
        }
        // Smooth over 10 ms: single milliseconds are too noisy to compare.
        let window = 10
        var smoothed = [Float](repeating: 0, count: count)
        var sum: Float = 0
        for index in 0..<count {
            sum += loudness[index]
            if index >= window { sum -= loudness[index - window] }
            smoothed[index] = sum / Float(min(index + 1, window))
        }
        var onsets = [Float](repeating: 0, count: count)
        for index in 1..<count {
            onsets[index] = max(smoothed[index] - smoothed[index - 1], 0)
        }
        let mean = vDSP.mean(onsets)
        return vDSP.add(-mean, onsets)
    }

    /// Finds the lag between two envelopes by cross-correlation over 0 to 2.5 s.
    /// - Parameters:
    ///   - sent: Envelope of the tap's recording.
    ///   - heard: Envelope of the microphone's recording.
    ///   - heardStartOffset: Seconds between the first sample of `sent` and the first sample of `heard`
    ///     (positive if the microphone recording started later).
    /// - Returns: Nil if no lag stands out clearly: too quiet, a noisy room, or headphones.
    public static func estimate(sent: [Float], heard: [Float], heardStartOffset: Double = 0) -> Estimate? {
        let offset = Int((heardStartOffset * envelopeRate).rounded())
        let maxLag = Int(AudioDelaySetting.range.upperBound * envelopeRate)
        guard sent.count > maxLag, heard.count > maxLag else { return nil }

        var correlation = [Float](repeating: 0, count: maxLag + 1)
        sent.withUnsafeBufferPointer { sentPointer in
            heard.withUnsafeBufferPointer { heardPointer in
                for lag in 0...maxLag {
                    // sent[t] lines up with heard[t + lag - offset].
                    let shift = lag - offset
                    let sentStart = max(0, -shift)
                    let heardStart = max(0, shift)
                    let length = min(sent.count - sentStart, heard.count - heardStart)
                    guard length > 0 else { continue }
                    var dot: Float = 0
                    let sentSlice = sentPointer.baseAddress! + sentStart
                    let heardSlice = heardPointer.baseAddress! + heardStart
                    vDSP_dotpr(sentSlice, 1, heardSlice, 1, &dot, vDSP_Length(length))
                    correlation[lag] = dot / Float(length)
                }
            }
        }

        guard let peak = correlation.indices.max(by: { correlation[$0] < correlation[$1] }), correlation[peak] > 0 else { return nil }
        let others = correlation.indices.filter { abs($0 - peak) > peakWidth }.map { correlation[$0] }
        guard others.count > 2 else { return nil }
        let mean = vDSP.mean(others)
        let deviation = sqrt(vDSP.meanSquare(vDSP.add(-mean, others)))
        guard deviation > 0 else { return nil }
        let zScore = (correlation[peak] - mean) / deviation
        let runnerUp = others.max() ?? 0
        guard zScore >= minimumZScore, runnerUp <= 0 || correlation[peak] / runnerUp >= minimumPeakRatio else { return nil }
        return Estimate(delay: Double(peak) / envelopeRate, clarity: Double(zScore))
    }

    /// A few seconds of audio from one source.
    public struct Recording: Sendable {
        /// Mono samples.
        public var samples: [Float]
        public var sampleRate: Double
        /// When the first sample was captured, in seconds on a clock both recordings share.
        public var start: TimeInterval

        public init(samples: [Float], sampleRate: Double, start: TimeInterval = 0) {
            self.samples = samples
            self.sampleRate = sampleRate
            self.start = start
        }
    }

    /// The whole measurement: both recordings in, the delay to save out.
    /// - Parameter inputLatency: The microphone's own latency in seconds, which is not part of the speakers' delay.
    public static func delay(sent: Recording, heard: Recording, inputLatency: Double = 0) -> Double? {
        let estimate = estimate(
            sent: envelope(sent.samples, sampleRate: sent.sampleRate),
            heard: envelope(heard.samples, sampleRate: heard.sampleRate),
            heardStartOffset: heard.start - sent.start
        )
        return estimate.map { AudioDelaySetting.normalized($0.delay - inputLatency) }
    }
}
