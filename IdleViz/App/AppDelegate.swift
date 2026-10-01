import AppKit
import IdleVizCore
import os

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "app")
    private let windowController = WindowController()
    let menuBarIcon = MenuBarIcon()
    private lazy var presets = PresetController(page: windowController.page)
    private lazy var settings = SettingsWindowController(
        presets: presets,
        openNow: { [weak self] in self?.open(from: .settings) }
    )
    private var triggers: Triggers?
    private var spotify: SpotifyInfo?
    private let audio = AudioPump()
    private var observers: [NSObjectProtocol] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        UserDefaults.standard.register(defaults: [IdleTimeoutSetting.key: IdleTimeoutSetting.defaultMinutes])
        // Created at launch, so the stored preset controls reach the page as soon as it loads.
        _ = presets
        let spotify = SpotifyInfo()
        self.spotify = spotify
        spotify.onOverlay = { [weak self] payload in self?.windowController.page.show(payload) }
        audio.onFrame = { [weak self] frame in self?.windowController.page.send(audioFrame: frame) }
        audio.spotifyIsPlaying = { spotify.current?.nowPlaying.state == .playing }
        // The tap and the status checks only run while the window is open.
        windowController.onOpen = { [weak self] in
            spotify.startResync()
            self?.audio.start()
            self?.windowController.page.startStatusChecks()
        }
        windowController.onClose = { [weak self] in
            spotify.stopResync()
            self?.audio.stop()
            self?.windowController.page.stopStatusChecks()
        }
        closeWhenTheDisplayMayHaveChanged()
        // Before anything can open settings, which makes the app regular.
        if let screen = NSScreen.screens.first { windowController.prepare(on: screen) }
        triggers = Triggers(onTrigger: { [weak self] source in self?.open(from: source) })
        #if DEBUG
        // Launch argument `-IdleVizShowSettings YES` opens the settings window at launch, for working on it.
        if UserDefaults.standard.bool(forKey: "IdleVizShowSettings") { showSettings() }
        #endif
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

    /// After sleep or a change of displays the main display may be a different one, so close
    /// instead of staying on a screen that may be gone.
    private func closeWhenTheDisplayMayHaveChanged() {
        let close: @Sendable (Notification) -> Void = { [weak self] _ in
            MainActor.assumeIsolated { self?.windowController.close() }
        }
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.willSleepNotification, object: nil, queue: .main, using: close
        ))
        observers.append(NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main, using: close
        ))
    }

    func showSettings() {
        settings.show()
    }

    private func open(from source: TriggerSource) {
        // With dismiss turned off (debug switch), a second manual trigger is the only way to close.
        if windowController.isOpen {
            if source.isManual && !windowController.dismissEnabled { windowController.close() }
            return
        }
        // Manual triggers mean someone is at the Mac, so only the idle trigger checks these.
        if !source.isManual, let skip = IdleWatcher.skip() {
            log.notice("Not opening on idle: \(String(describing: skip), privacy: .public)")
            return
        }
        guard let spotify else { return }
        Task {
            if let refusal = await spotify.openRefusal() {
                log.notice("Not opening via \(source.rawValue, privacy: .public): \(refusal.rawValue, privacy: .public)")
                if source.isManual { menuBarIcon.flash() }
                return
            }
            // Another trigger may have opened it while Spotify was being asked.
            guard !windowController.isOpen, let screen = NSScreen.screens.first else { return }
            windowController.open(on: screen, source: source)
        }
    }
}
