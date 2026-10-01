import Accelerate
import Foundation

/// Turns the newest 1024 stereo samples into an `AudioFrame`: automatic gain, a 64-band
/// spectrum with a noise floor and smoothing, and Butterchurn's byte arrays.
public struct AudioAnalyzer {
    /// Band levels map this range of decibels to 0...1. A full-scale sine is 0 dB.
    static let floorDecibels: Float = -65
    static let rangeDecibels: Float = 55
    /// How far a band moves towards a higher and a lower value each frame: fast attack, slow release.
    static let attack: Float = 0.7
    static let release: Float = 0.12
    static let lowestHz = 40.0
    static let highestHz = 16_000.0

    private let count = AudioFrame.sampleCount
    private let window: [Float]
    private let fft: vDSP.FFT<DSPSplitComplex>
    private var gainControl = AutoGain()
    private var bands = [Float](repeating: 0, count: AudioFrame.bandCount)
    private var sequence: UInt32 = 0

    public init() {
        window = vDSP.window(ofType: Float.self, usingSequence: .hanningDenormalized, count: count, isHalfWindow: false)
        // 1024 = 2^10. The initializer only fails for sizes it doesn't support.
        fft = vDSP.FFT(log2n: 10, radix: .radix2, ofType: DSPSplitComplex.self)!
    }

    /// The gain currently applied, for logging.
    public var gain: Float { gainControl.gain }

    /// - Parameters:
    ///   - left: The newest 1024 samples of the left channel, oldest first.
    ///   - right: The same for the right channel.
    ///   - sampleRate: The tap's sample rate.
    ///   - seconds: Time since the last frame, for the automatic gain.
    public mutating func analyze(left: [Float], right: [Float], sampleRate: Double, seconds: Float = 1 / 60) -> AudioFrame {
        precondition(left.count == count && right.count == count, "analyze needs \(count) samples per channel")
        defer { sequence &+= 1 }

        var mono = vDSP.add(left, right)
        vDSP.multiply(0.5, mono, result: &mono)
        let gain = gainControl.update(rms: vDSP.rootMeanSquare(mono), seconds: seconds)
        vDSP.multiply(gain, mono, result: &mono)
        vDSP.clip(mono, to: -1...1, result: &mono)

        updateBands(mono: mono, sampleRate: sampleRate)
        return AudioFrame(
            sequence: sequence,
            sampleRate: Float(sampleRate),
            bass: vDSP.mean(bands[0..<8]),
            mid: vDSP.mean(bands[8..<30]),
            treble: vDSP.mean(bands[30..<64]),
            rms: min(vDSP.rootMeanSquare(mono), 1),
            bands: bands,
            waveform: mono,
            monoBytes: Self.bytes(mono, gain: 1),
            leftBytes: Self.bytes(left, gain: gain),
            rightBytes: Self.bytes(right, gain: gain)
        )
    }

    /// Unsigned 8-bit samples around 128, as Web Audio's `getByteTimeDomainData` would give Butterchurn.
    static func bytes(_ samples: [Float], gain: Float) -> [UInt8] {
        samples.map { sample in
            let scaled = (sample * gain * 128).rounded()
            return UInt8(min(max(scaled + 128, 0), 255))
        }
    }

    private mutating func updateBands(mono: [Float], sampleRate: Double) {
        let magnitudes = spectrum(of: mono)
        let binHz = sampleRate / Double(count)
        let lastBin = Double(count / 2 - 1)
        let ratio = Self.highestHz / Self.lowestHz
        for band in 0..<AudioFrame.bandCount {
            let low = Self.lowestHz * pow(ratio, Double(band) / Double(AudioFrame.bandCount)) / binHz
            let high = Self.lowestHz * pow(ratio, Double(band + 1) / Double(AudioFrame.bandCount)) / binHz
            let magnitude = Self.magnitude(in: magnitudes, from: min(low, lastBin), to: min(high, lastBin))
            let decibels = 20 * log10(max(magnitude, 1e-9))
            let level = min(max((decibels - Self.floorDecibels) / Self.rangeDecibels, 0), 1)
            let previous = bands[band]
            let next = previous + (level - previous) * (level > previous ? Self.attack : Self.release)
            // Snap to zero so silence becomes exact silence instead of fading forever.
            bands[band] = next < 0.0005 ? 0 : next
        }
    }

    /// Magnitude of each frequency bin, scaled so a full-scale sine reads 1.
    private func spectrum(of mono: [Float]) -> [Float] {
        let half = count / 2
        var windowed = vDSP.multiply(mono, window)
        var real = [Float](repeating: 0, count: half)
        var imaginary = [Float](repeating: 0, count: half)
        var magnitudes = [Float](repeating: 0, count: half)
        real.withUnsafeMutableBufferPointer { realPointer in
            imaginary.withUnsafeMutableBufferPointer { imaginaryPointer in
                var split = DSPSplitComplex(realp: realPointer.baseAddress!, imagp: imaginaryPointer.baseAddress!)
                windowed.withUnsafeMutableBytes { raw in
                    // Reads the real samples as (even, odd) pairs, the packed input vDSP's real FFT expects.
                    vDSP_ctoz(raw.bindMemory(to: DSPComplex.self).baseAddress!, 2, &split, 1, vDSP_Length(half))
                }
                fft.forward(input: split, output: &split)
                vDSP.absolute(split, result: &magnitudes)
            }
        }
        // vDSP's real FFT returns twice the mathematical transform, and the Hann window halves
        // a sine's peak: 2 × (N / 2) × 0.5 × amplitude = amplitude × N / 2.
        vDSP.divide(magnitudes, Float(half), result: &magnitudes)
        return magnitudes
    }

    func spectrumForTesting(_ mono: [Float]) -> [Float] { spectrum(of: mono) }

    /// The average magnitude between two fractional bin positions. Low bands are narrower than
    /// one bin, so those read a value interpolated between the two nearest bins.
    static func magnitude(in magnitudes: [Float], from low: Double, to high: Double) -> Float {
        let first = Int(low.rounded(.up))
        let last = Int(high.rounded(.down))
        if last > first {
            return vDSP.mean(magnitudes[first...last])
        }
        let centre = (low + high) / 2
        let below = Int(centre.rounded(.down))
        let above = min(below + 1, magnitudes.count - 1)
        let fraction = Float(centre - Double(below))
        return magnitudes[below] * (1 - fraction) + magnitudes[above] * fraction
    }
}
