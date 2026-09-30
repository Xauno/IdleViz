import IdleVizCore
import KeyboardShortcuts

extension KeyboardShortcuts.Name {
    static let openVisualizer = Self("openVisualizer", initial: .init(.v, modifiers: [.control, .option]))
}

/// Manual triggers. The idle trigger joins in step 6, the open rules in step 4.
@MainActor
final class Triggers {
    init(onTrigger: @escaping @MainActor (TriggerSource) -> Void) {
        // Key up, not key down: the key is already released when the window opens,
        // so only the modifiers can still come up during the grace period.
        KeyboardShortcuts.onKeyUp(for: .openVisualizer) {
            onTrigger(.hotkey)
        }
    }
}
