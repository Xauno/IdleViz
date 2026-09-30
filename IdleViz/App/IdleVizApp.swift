import SwiftUI

@main
struct IdleVizApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        MenuBarExtra {
            MenuBarView(openSettings: { appDelegate.showSettings() })
        } label: {
            Image(systemName: appDelegate.menuBarIcon.symbol)
                .accessibilityLabel("IdleViz")
        }
        .menuBarExtraStyle(.window)
    }
}
