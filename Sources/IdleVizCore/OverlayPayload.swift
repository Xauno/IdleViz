import Foundation

/// What the page's `window.nowPlaying(json)` receives. The page never uses the network,
/// so the artwork travels inside as a `data:` URL.
public struct OverlayPayload: Encodable, Sendable, Equatable {
    public var id: String
    public var state: String
    public var content: String
    public var title: String
    public var artist: String
    public var artwork: String?
    /// True while the artwork is still downloading, so the page doesn't flash the placeholder.
    public var artworkPending: Bool
    public var durationMs: Int
    /// Seconds.
    public var position: Double

    public init(snapshot: SpotifySnapshot, artwork: Data?, artworkPending: Bool) {
        let item = snapshot.nowPlaying
        id = item.spotifyURL
        state = item.state.rawValue
        content = snapshot.content.rawValue
        title = item.name
        artist = item.artist
        self.artwork = artwork.flatMap(Self.dataURL(for:))
        self.artworkPending = self.artwork == nil && artworkPending
        durationMs = item.durationMS
        position = item.position
    }

    /// JSON for `window.nowPlaying`, or `null` when there's no track. `JSONEncoder` escapes quotes and
    /// backslashes in titles; U+2028/U+2029 are escaped too, since older JavaScript rejects them raw.
    public static func script(for payload: OverlayPayload?) -> String {
        let json = payload.flatMap { try? JSONEncoder().encode($0) }.flatMap { String(data: $0, encoding: .utf8) }
        let safe = (json ?? "null")
            .replacingOccurrences(of: "\u{2028}", with: "\\u2028")
            .replacingOccurrences(of: "\u{2029}", with: "\\u2029")
        return "window.nowPlaying?.(\(safe))"
    }

    /// A `data:` URL for JPEG, PNG or WebP bytes; nil for anything else.
    public static func dataURL(for data: Data) -> String? {
        let bytes = [UInt8](data.prefix(12))
        let type: String
        if bytes.starts(with: [0xFF, 0xD8, 0xFF]) {
            type = "image/jpeg"
        } else if bytes.starts(with: [0x89, 0x50, 0x4E, 0x47]) {
            type = "image/png"
        } else if bytes.count >= 12, bytes[0..<4] == [0x52, 0x49, 0x46, 0x46], bytes[8..<12] == [0x57, 0x45, 0x42, 0x50] {
            type = "image/webp"
        } else {
            return nil
        }
        return "data:\(type);base64,\(data.base64EncodedString())"
    }
}
