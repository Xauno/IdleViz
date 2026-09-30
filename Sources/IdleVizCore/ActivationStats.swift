import Foundation

/// Counts how often macOS refuses to activate the app when the visualizer opens.
/// Activation is cooperative, so a refused open leaves the window without key
/// focus: keys then reach the app behind it and the cursor can't be hidden.
public struct ActivationStats: Sendable, Equatable {
    public private(set) var opens = 0
    public private(set) var refused = 0

    public init() {}

    public mutating func record(activated: Bool) {
        opens += 1
        if !activated { refused += 1 }
    }

    public var summary: String {
        "activation refused \(refused) of \(opens) opens"
    }
}
