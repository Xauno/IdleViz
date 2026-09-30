import AppKit
import IdleVizCore
import os

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "app")
    private let windowController = WindowController()
    private lazy var settings = SettingsWindowController(openNow: { [weak self] in self?.open(from: .settings) })
    private var triggers: Triggers?

    func applicationDidFinishLaunching(_ notification: Notification) {
        triggers = Triggers(onTrigger: { [weak self] source in self?.open(from: source) })
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            guard let command = URLCommand(url: url) else {
                log.info("Ignoring unknown URL \(url.absoluteString, privacy: .public)")
                continue
            }
            switch command {
            case .open: open(from: .urlScheme)
            }
        }
    }

    func showSettings() {
        settings.show()
    }

    private func open(from source: TriggerSource) {
        // With dismiss turned off (debug switch), a second trigger is the only way to close.
        if windowController.isOpen {
            if !windowController.dismissEnabled { windowController.close() }
            return
        }
        guard let screen = NSScreen.screens.first else { return }
        windowController.open(on: screen, source: source)
    }
}
