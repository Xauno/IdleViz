import Foundation

/// The few most recently used album covers, so skipping back and forth doesn't download again.
public struct ArtworkCache: Sendable {
    public let capacity: Int
    /// Most recently used last.
    private var entries: [(url: String, data: Data)] = []

    public init(capacity: Int = 5) {
        precondition(capacity > 0)
        self.capacity = capacity
    }

    public var urls: [String] { entries.map(\.url) }

    public mutating func image(for url: String) -> Data? {
        guard let index = entries.firstIndex(where: { $0.url == url }) else { return nil }
        let entry = entries.remove(at: index)
        entries.append(entry)
        return entry.data
    }

    public mutating func insert(_ data: Data, for url: String) {
        entries.removeAll { $0.url == url }
        entries.append((url, data))
        if entries.count > capacity { entries.removeFirst(entries.count - capacity) }
    }
}
