import AppKit
import IdleVizCore
import KeyboardShortcuts
import SwiftUI

/// Separate settings window, per mockups.html section 2. So far it has the idle
/// timeout, the hotkey and Open now; the other rows arrive with the features they control.
@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    private let openNow: () -> Void
    private var window: NSWindow?

    init(openNow: @escaping () -> Void) {
        self.openNow = openNow
    }

    func show() {
        let window = window ?? makeWindow()
        self.window = window
        // A regular app while the window is open, so it can take focus and ⌘Q works.
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
    }

    private func makeWindow() -> NSWindow {
        let hosting = NSHostingController(rootView: SettingsView(openNow: openNow))
        hosting.sizingOptions = []
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 560),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Settings"
        window.contentViewController = hosting
        window.setContentSize(NSSize(width: 340, height: 560))
        window.collectionBehavior = [.fullScreenNone]
        window.standardWindowButton(.zoomButton)?.isEnabled = false
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
        return window
    }
}

struct SettingsView: View {
    let openNow: () -> Void
    @AppStorage(IdleTimeoutSetting.key) private var idleTimeout = IdleTimeoutSetting.defaultMinutes

    var body: some View {
        Form {
            Section("General") {
                Picker(selection: $idleTimeout) {
                    ForEach(IdleTimeoutSetting.choices, id: \.self) { Text("\($0) min").tag($0) }
                    Text("Off").tag(0)
                } label: {
                    Text("Start after idle")
                    Text("Needs a Spotify track")
                }
                KeyboardShortcuts.Recorder("Open hotkey", name: .openVisualizer)
                LabeledContent("Open now") {
                    Button("Open", action: openNow)
                }
            }
        }
        .formStyle(.grouped)
    }
}
