import Foundation

/// The manual delay test: a beep every second, and a panel that lights up one audio delay after
/// each beep was sent to the speakers. When the light and the sound land together, the delay is right.
///
/// Every fourth beep is higher and its flash has another colour, so a flash can be told apart
/// from the one a beep earlier or later even when the delay is longer than the gap between beeps.
public enum BeepTest {
    /// Seconds from one beep to the next.
    public static let period = 1.0
    /// Every this many beeps, one is marked.
    public static let accentEvery = 4
    public static let beepSeconds = 0.06
    /// The panel stays lit a little longer than the beep sounds, so it's easy to see.
    public static let flashSeconds = 0.12
    public static let frequency = 880.0
    public static let accentFrequency = 1760.0
    static let volume: Float = 0.4
    /// The beep fades in and out over this long, so it doesn't click.
    static let fadeSeconds = 0.005

    /// A lit panel.
    public struct Flash: Sendable, Equatable {
        /// True for the marked beep's flash.
        public var accent: Bool

        public init(accent: Bool) {
            self.accent = accent
        }
    }

    /// Whether the beep with this index, counted from 0, is a marked one.
    public static func isAccent(_ index: Int) -> Bool {
        index % accentEvery == 0
    }

    /// When the beep with this index is sent to the speakers.
    public static func beepTime(_ index: Int, start: TimeInterval) -> TimeInterval {
        start + Double(index) * period
    }

    /// The flash showing at `time`, or nil while the panel is dark.
    /// - Parameters:
    ///   - start: When the first beep was sent to the speakers, on the same clock as `time`.
    ///   - delay: The audio delay being tried.
    public static func flash(at time: TimeInterval, start: TimeInterval, delay: TimeInterval) -> Flash? {
        let sinceFirst = time - start - delay
        guard sinceFirst >= 0 else { return nil }
        let index = Int(sinceFirst / period)
        guard sinceFirst - Double(index) * period < flashSeconds else { return nil }
        return Flash(accent: isAccent(index))
    }

    /// One beep as mono samples: a sine tone with a short fade at both ends.
    public static func samples(accent: Bool, sampleRate: Double) -> [Float] {
        guard sampleRate > 0 else { return [] }
        let count = Int(beepSeconds * sampleRate)
        let fade = max(1, Int(fadeSeconds * sampleRate))
        let step = 2 * Double.pi * (accent ? accentFrequency : frequency) / sampleRate
        return (0..<count).map { index in
            let envelope = min(1, Float(min(index, count - 1 - index)) / Float(fade))
            return Float(sin(Double(index) * step)) * envelope * volume
        }
    }
}
