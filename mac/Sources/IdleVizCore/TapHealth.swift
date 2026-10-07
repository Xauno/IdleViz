import Foundation

/// There's no API to ask whether System Audio Recording is allowed. A tap without permission
/// delivers only exact zeros, or nothing, so that pattern while Spotify says it's playing is
/// the sign. A paused Spotify sends zeros too, which is why only playing time counts.
/// The same pattern shows up when Spotify plays on another device (Spotify Connect).
public struct TapHealth: Sendable {
    public static let suspectAfterSeconds = 5

    private var silentSeconds = 0
    /// True while the tap looks like it has no permission.
    public private(set) var suspected = false

    public init() {}

    /// Call once a second with what the tap delivered in that second.
    /// - Returns: True the moment the tap first looks denied, so it can be logged once.
    public mutating func record(buffers: Int, zeroBuffers: Int, spotifyPlaying: Bool) -> Bool {
        guard spotifyPlaying else {
            silentSeconds = 0
            return false
        }
        if buffers > zeroBuffers {
            silentSeconds = 0
            suspected = false
            return false
        }
        silentSeconds += 1
        guard silentSeconds >= Self.suspectAfterSeconds, !suspected else { return false }
        suspected = true
        return true
    }
}
