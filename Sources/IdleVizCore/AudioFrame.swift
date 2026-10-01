import Foundation

/// Everything the page needs for one frame of visuals, computed from Spotify's audio only.
public struct AudioFrame: Sendable, Equatable {
    public static let sampleCount = 1024
    public static let bandCount = 64
    public static let headerLength = 24
    /// 24-byte header, 64 bands and a 1024-sample waveform as `f32`, then three 1024-byte arrays.
    public static let byteLength = headerLength + bandCount * 4 + sampleCount * 4 + 3 * sampleCount

    public var sequence: UInt32
    public var sampleRate: Float
    /// Averages of bands 0–7, 8–29 and 30–63, each 0...1.
    public var bass: Float
    public var mid: Float
    public var treble: Float
    /// Loudness after automatic gain, 0...1.
    public var rms: Float
    /// 64 log-spaced bands from about 40 Hz to 16 kHz, 0...1, noise-floored and smoothed.
    public var bands: [Float]
    /// Mono samples after automatic gain, −1...1.
    public var waveform: [Float]
    /// Butterchurn's input: time-domain samples as unsigned bytes (128 is silence).
    public var monoBytes: [UInt8]
    public var leftBytes: [UInt8]
    public var rightBytes: [UInt8]

    /// A frame of silence.
    public static func silence(sequence: UInt32, sampleRate: Float) -> AudioFrame {
        AudioFrame(
            sequence: sequence, sampleRate: sampleRate, bass: 0, mid: 0, treble: 0, rms: 0,
            bands: [Float](repeating: 0, count: bandCount),
            waveform: [Float](repeating: 0, count: sampleCount),
            monoBytes: [UInt8](repeating: 128, count: sampleCount),
            leftBytes: [UInt8](repeating: 128, count: sampleCount),
            rightBytes: [UInt8](repeating: 128, count: sampleCount)
        )
    }

    /// The binary form sent to the page, little-endian. `web/audio-frame.js` unpacks it:
    /// `u32` sequence, `f32` sample rate, bass, mid, treble, rms, `f32[64]` bands,
    /// `f32[1024]` waveform, `u8[1024]` mono, left, right.
    public func packed() -> Data {
        var data = Data(capacity: Self.byteLength)
        withUnsafeBytes(of: sequence.littleEndian) { data.append(contentsOf: $0) }
        for value in [sampleRate, bass, mid, treble, rms] + bands + waveform {
            withUnsafeBytes(of: value.bitPattern.littleEndian) { data.append(contentsOf: $0) }
        }
        data.append(contentsOf: monoBytes)
        data.append(contentsOf: leftBytes)
        data.append(contentsOf: rightBytes)
        return data
    }

    /// The call that hands a packed frame to the page. Base64 only uses characters that are safe
    /// inside a JavaScript string. The `?.` keeps a frame sent before the page has loaded from throwing.
    public static func script(for packed: Data) -> String {
        "window.audioFrame?.('\(packed.base64EncodedString())')"
    }
}
