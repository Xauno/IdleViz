import AppKit
import IdleVizCore
import os

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "app")
    private let windowController = WindowController()
    let menuBarIcon = MenuBarIcon()
    private lazy var settings = SettingsWindowController(openNow: { [weak self] in self?.open(from: .settings) })
    private var triggers: Triggers?
    private var spotify: SpotifyInfo?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let spotify = SpotifyInfo()
        self.spotify = spotify
        spotify.onOverlay = { [weak self] payload in self?.windowController.page.show(payload) }
        windowController.onOpen = { spotify.startResync() }
        windowController.onClose = { spotify.stopResync() }
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
        guard let spotify else { return }
        Task {
            if let refusal = await spotify.openRefusal() {
                log.notice("Not opening via \(source.rawValue, privacy: .public): \(refusal.rawValue, privacy: .public)")
                menuBarIcon.flash()
                return
            }
            // Another trigger may have opened it while Spotify was being asked.
            guard !windowController.isOpen, let screen = NSScreen.screens.first else { return }
            windowController.open(on: screen, source: source)
        }
    }
}
