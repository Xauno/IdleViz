import AppKit
import IdleVizCore
import SwiftUI

/// The Keys section in settings: the like, skip and block keys, each recorded by pressing it.
struct KeyControls: View {
    @AppStorage(VisualizerKeys.likeKey) private var likeKey = VisualizerKeys.defaultLike
    @AppStorage(VisualizerKeys.skipKey) private var skipKey = VisualizerKeys.defaultSkip
    @AppStorage(VisualizerKeys.blockKey) private var blockKey = VisualizerKeys.defaultBlock
    @AppStorage(MultiDisplaySettings.closeOnInputKey) private var closeOnInput = true

    var body: some View {
        // What the visualizer will use: a key two rows share counts for the first of them only.
        let keys = VisualizerKeys(
            like: VisualizerKeys.code(stored: likeKey, fallback: VisualizerKeys.defaultLike),
            skip: VisualizerKeys.code(stored: skipKey, fallback: VisualizerKeys.defaultSkip),
            block: VisualizerKeys.code(stored: blockKey, fallback: VisualizerKeys.defaultBlock)
        )
        Group {
            KeyRecorder(
                title: "Like key", hint: "Favorites what's on screen",
                key: keys.like, taken: [keys.skip, keys.block], stored: $likeKey
            )
            KeyRecorder(
                title: "Skip key", hint: "Next visualizer, in Shuffle",
                key: keys.skip, taken: [keys.like, keys.block], stored: $skipKey
            )
            KeyRecorder(
                title: "Block key", hint: "Blocks what's on screen, then skips",
                key: keys.block, taken: [keys.like, keys.skip], stored: $blockKey
            )
        }
        // No key is watched while input is ignored.
        .disabled(!closeOnInput)
    }
}

/// One key row: click the button, which then says "Press a key", and press one. Esc, or a click
/// anywhere, keeps the key the row had. The ✕ button turns the key off.
private struct KeyRecorder: View {
    let title: String
    let hint: String
    /// The key in use, or nil for none.
    let key: UInt16?
    /// The keys of the other rows, which this row refuses.
    let taken: [UInt16?]
    @Binding var stored: Int
    @Environment(\.isEnabled) private var isEnabled
    @State private var recording = false
    @State private var refused = false
    @State private var monitor: Any?

    var body: some View {
        LabeledContent {
            HStack(spacing: 4) {
                Button(buttonText) { start() }
                    .accessibilityLabel("\(title): \(VisualizerKeys.label(key))")
                Button {
                    stop()
                    stored = VisualizerKeys.off
                } label: {
                    Image(systemName: "xmark.circle.fill")
                }
                .buttonStyle(.borderless)
                .disabled(key == nil)
                .help("Turn the \(title.lowercased()) off")
                .accessibilityLabel("Turn the \(title.lowercased()) off")
            }
        } label: {
            Text(title)
            Text(isEnabled ? hint : "Needs Close on input")
        }
        .onDisappear { stop() }
    }

    private var buttonText: String {
        guard recording else { return VisualizerKeys.label(key) }
        return refused ? "In use, press another" : "Press a key"
    }

    private func start() {
        guard !recording else { return }
        recording = true
        refused = false
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .leftMouseDown, .rightMouseDown]) { event in
            MainActor.assumeIsolated { handle(event) } ? nil : event
        }
    }

    private func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        recording = false
        refused = false
    }

    /// Whether the recorder used the event. If not, it goes on to the window.
    private func handle(_ event: NSEvent) -> Bool {
        guard event.type == .keyDown else {
            // A click elsewhere keeps the old key, and still does what it would have done.
            stop()
            return false
        }
        let code = event.keyCode
        if code == 53 {
            stop()
        } else if !VisualizerKeys.canBe(Int(code)) {
            // A modifier never gets here (it is a flags change, not a key down), so this is only a guard.
        } else if taken.contains(code) {
            refused = true
        } else {
            stored = Int(code)
            stop()
        }
        return true
    }
}
