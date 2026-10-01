import SwiftUI

@main
struct IdleVizApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        MenuBarExtra {
            MenuBarView(permissions: appDelegate.permissions, openSettings: { appDelegate.showSettings() })
        } label: {
            Image(nsImage: appDelegate.menuBarIcon.image)
                .accessibilityLabel(appDelegate.menuBarIcon.warning ? "IdleViz, a permission is missing" : "IdleViz")
        }
        .menuBarExtraStyle(.window)
    }
}
