import SwiftUI

@main
struct IdleVizApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        MenuBarExtra("IdleViz", systemImage: "waveform") {
            MenuBarView(openSettings: { appDelegate.showSettings() })
        }
        .menuBarExtraStyle(.window)
    }
}
