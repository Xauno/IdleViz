import Foundation

/// One display, reduced to what decides whether the visualizer still fits it.
public struct Display: Sendable, Equatable {
    /// The `CGDirectDisplayID`.
    public let id: UInt32
    /// The whole display in screen coordinates, Dock and menu bar included.
    public let frame: CGRect
    /// Pixels per point.
    public let scale: Double

    public init(id: UInt32, frame: CGRect, scale: Double) {
        self.id = id
        self.frame = frame
        self.scale = scale
    }
}

/// The connected displays in macOS's order, the main display first.
public struct DisplayLayout: Sendable, Equatable {
    public let displays: [Display]

    public init(displays: [Display]) {
        self.displays = displays
    }
}

/// Decides whether a screen-parameters change closes the visualizer. Pure logic.
///
/// macOS posts that change for more than the displays: the Dock growing or shrinking by a tile
/// (a Handoff suggestion from a phone that just got a message, an app opening) and the menu bar
/// showing or hiding post it too. Those leave the layout as it was, so the window stays open.
public struct DisplayTracker: Sendable {
    public private(set) var layout: DisplayLayout

    public init(layout: DisplayLayout) {
        self.layout = layout
    }

    /// Call on each screen-parameters change with the layout as it is now.
    public mutating func shouldClose(on current: DisplayLayout) -> Bool {
        guard current != layout else { return false }
        layout = current
        return true
    }
}
