import Foundation

/// Why the visualizer closes. Each reason fades out at its own speed.
public enum CloseReason: String, Sendable, Equatable {
    /// Any mouse, key or trackpad input.
    case input
    /// The keep-awake limit was reached.
    case keepAwakeLimit
    /// The Mac is going to sleep or the displays changed.
    case displayChanged

    public var fadeSeconds: TimeInterval {
        switch self {
        // Someone is at the Mac and wants it back, so this is as quick as the macOS screensaver.
        case .input: 0.25
        // Nobody is at the Mac, so it leaves gently.
        case .keepAwakeLimit: 1.5
        // The screen it's on may be gone before a fade could finish.
        case .displayChanged: 0
        }
    }
}

public enum VisualizerFade {
    public static let openSeconds: TimeInterval = 0.6
}
