import Foundation

/// Slow automatic gain. The tap hears Spotify after its own volume slider, so a low Spotify
/// volume would mean weak visuals. This follows the loudness and scales it to a steady level.
public struct AutoGain: Sendable {
    /// The loudness (RMS) the loud parts of a song are brought to.
    public static let target: Float = 0.2
    /// Below this RMS (about −60 dBFS) the input counts as silence: the gain is held, not raised,
    /// so pauses, fades and gaps between tracks stay quiet.
    public static let gate: Float = 0.001
    public static let minGain: Float = 0.5
    public static let maxGain: Float = 32
    /// Seconds for the followed loudness to rise to a louder passage, and to fall to a quieter one.
    /// Falling is slow on purpose: a quiet bridge should look quiet, not get turned up.
    static let riseSeconds: Float = 0.5
    static let fallSeconds: Float = 5

    public private(set) var gain: Float = 1
    /// Starts at the target (a gain of 1), so the fade-in after a pause isn't mistaken for a quiet song.
    private var level = AutoGain.target

    public init() {}

    /// Feeds the loudness of the newest samples and returns the gain to apply to them.
    /// - Parameters:
    ///   - rms: RMS of the samples, before gain.
    ///   - seconds: Time since the last call.
    public mutating func update(rms: Float, seconds: Float) -> Float {
        guard rms.isFinite, rms >= Self.gate else { return gain }
        let time = rms > level ? Self.riseSeconds : Self.fallSeconds
        level += (rms - level) * (1 - exp(-max(seconds, 0) / time))
        gain = min(max(Self.target / level, Self.minGain), Self.maxGain)
        return gain
    }
}
