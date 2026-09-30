import IdleVizCore
import KeyboardShortcuts

extension KeyboardShortcuts.Name {
    static let openVisualizer = Self("openVisualizer", initial: .init(.v, modifiers: [.control, .option]))
}

/// The hotkey and the idle trigger. The URL scheme arrives through the app delegate.
@MainActor
final class Triggers {
    private let idle: IdleWatcher

    init(onTrigger: @escaping @MainActor (TriggerSource) -> Void) {
        idle = IdleWatcher { onTrigger(.idle) }
        idle.start()
        // Key up, not key down: the key is already released when the window opens,
        // so only the modifiers can still come up during the grace period.
        KeyboardShortcuts.onKeyUp(for: .openVisualizer) {
            onTrigger(.hotkey)
        }
    }
}
