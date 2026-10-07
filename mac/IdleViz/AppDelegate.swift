import AppKit
import IdleVizCore
import os

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "app")
    private let windowController = WindowController()
    let menuBarIcon = MenuBarIcon()
    let permissions = Permissions()
    private lazy var welcome = WelcomeWindowController(permissions: permissions)
    private lazy var presets = PresetController(page: windowController.page, library: PresetLibrary())
    private lazy var audioDelay = AudioDelayController(pump: audio)
    private lazy var displayOptions = DisplayOptions(page: windowController.page)
    private lazy var settings = SettingsWindowController(
        presets: presets,
        audioDelay: audioDelay,
        openNow: { [weak self] in self?.open(from: .settings) }
    )

    /// Shows one preset in the real visualizer, from the Favorites and Blocklist sheets. The page
    /// is held on it until the visualizer starts to close, or at once if the open is refused.
    private func preview(_ preset: String) {
        presets.preview(preset)
        // Already open (possible with "Close on input" off): only the preset changes.
        guard !windowController.isOpen else { return }
        open(from: .settings, onRefused: { [weak self] in self?.presets.endPreview() })
    }
    private var triggers: Triggers?
    private let power = PowerSource()
    private lazy var keepAwake = KeepAwake(
        limit: { [power] in TimingSettings(defaults: .standard).keepAwakeLimit(onBattery: power.onBattery) },
        onLimit: { [weak self] in
            // The Mac is still idle, so the idle trigger has to wait for new input.
            self?.triggers?.idle.waitForInput()
            self?.windowController.close(.keepAwakeLimit)
        }
    )
    private var spotify: SpotifyInfo?
    private let audio = AudioPump()
    private var observers: [NSObjectProtocol] = []
    private var displays = DisplayTracker(layout: .current)

    func applicationDidFinishLaunching(_ notification: Notification) {
        UserDefaults.standard.register(defaults: [
            IdleTimeoutSetting.key: IdleTimeoutSetting.defaultMinutes,
            KeepAwakeSetting.key: KeepAwakeSetting.defaultMinutes,
        ])
        // Created at launch, so the stored preset controls reach the page as soon as it loads.
        _ = presets
        _ = audioDelay
        _ = displayOptions
        let spotify = SpotifyInfo()
        self.spotify = spotify
        spotify.onOverlay = { [weak self] payload in self?.windowController.page.show(payload) }
        audio.onFrame = { [weak self] frame in self?.windowController.page.send(audioFrame: frame) }
        audio.spotifyIsPlaying = { spotify.current?.nowPlaying.state == .playing }
        audioDelay.spotifyIsPlaying = audio.spotifyIsPlaying
        audioDelay.pauseSpotify = { spotify.pause() }
        audioDelay.resumeSpotify = { spotify.play() }
        // The same delay holds back the audio frames and the progress bar.
        audioDelay.onChange = { [weak self] delay in
            self?.audio.delay = delay
            self?.windowController.page.send(audioDelay: delay)
        }
        audio.delay = audioDelay.delay
        windowController.page.send(audioDelay: audioDelay.delay)
        // The tap and the status checks only run while the window is open.
        windowController.onOpen = { [weak self] in
            spotify.startResync()
            self?.audio.start()
            self?.windowController.page.startStatusChecks()
            self?.keepAwake.start()
            self?.permissions.refresh()
        }
        // The Mac may sleep again as soon as the fade-out starts.
        // A preview ends here too, not when the window is gone: an open during the fade-out finishes
        // that close first, which would otherwise end the new preview.
        windowController.onClosing = { [weak self] in
            self?.keepAwake.stop()
            self?.presets.endPreview()
        }
        presets.onPreview = { [weak self] preset in self?.preview(preset) }
        windowController.onClose = { [weak self] in
            spotify.stopResync()
            self?.audio.stop()
            self?.windowController.page.stopStatusChecks()
        }
        windowController.onAction = { [weak self] action in self?.presets.perform(action) }
        closeWhenTheDisplayMayHaveChanged()
        startPermissions(spotify)
        // Before anything can open settings, which makes the app regular.
        windowController.prepare()
        prepareWhenTheDisplaySettingsChange()
        startTriggers()
        runDebugLaunchArguments()
    }

    private func startPermissions(_ spotify: SpotifyInfo) {
        permissions.spotifyIsRunning = { spotify.isRunning }
        permissions.startTap = { [audio] in audio.startPermissionTap() }
        permissions.stopTap = { [audio] in audio.stopPermissionTap() }
        permissions.onAutomationAnswered = { spotify.refresh() }
        permissions.showWelcome = { [weak self] in self?.welcome.show() }
        permissions.onChange = { [weak self] status in self?.menuBarIcon.warning = !status.missing.isEmpty }
        menuBarIcon.warning = !permissions.status.missing.isEmpty
        audio.mayTap = { [permissions] in permissions.status.audio != .notAsked }
        audio.onSuspected = { [permissions] suspected in permissions.tapSuspected(suspected) }
        permissions.startObserving()
        // Ask for anything macOS hasn't asked about now, while someone is at the Mac.
        Task { [weak self] in
            await self?.permissions.check()
            if self?.permissions.status.needsWelcome == true { self?.welcome.show() }
        }
    }

    private func startTriggers() {
        triggers = Triggers(
            idleTimeout: { [power] in TimingSettings(defaults: .standard).idleTimeout(onBattery: power.onBattery) },
            onTrigger: { [weak self] source in self?.open(from: source) }
        )
        power.onChange = { [weak self] in
            guard let self else { return }
            self.log.notice("Now on \(self.power.onBattery ? "battery" : "mains power", privacy: .public)")
            self.triggers?.idle.timeoutMayHaveChanged()
            self.keepAwake.limitMayHaveChanged()
        }
    }

    private func runDebugLaunchArguments() {
        #if DEBUG
        // Launch argument `-IdleVizShowSettings YES` opens the settings window at launch, for working on it.
        if UserDefaults.standard.bool(forKey: "IdleVizShowSettings") { showSettings() }
        // `-IdleVizShowWelcome YES` does the same for the welcome window.
        if UserDefaults.standard.bool(forKey: "IdleVizShowWelcome") { welcome.show() }
        // `-IdleVizDetectDelay YES` runs Detect delay a few seconds after launch, as the button would.
        if UserDefaults.standard.bool(forKey: "IdleVizDetectDelay") {
            Task { [weak self] in
                try? await Task.sleep(for: .seconds(4))
                self?.audioDelay.detect()
            }
        }
        // `-IdleVizPreview "bundled:Geiss - Swirlie 5"` previews that preset a moment after launch, as a Preview button would.
        if let preset = UserDefaults.standard.string(forKey: "IdleVizPreview") {
            Task { [weak self] in
                try? await Task.sleep(for: .seconds(3))
                self?.preview(preset)
            }
        }
        // `-IdleVizOpenAtLaunch YES` opens the visualizer a moment after launch. Unlike `open idleviz://open`,
        // it can't be routed to another copy of the app that happens to be on disk.
        if UserDefaults.standard.bool(forKey: "IdleVizOpenAtLaunch") {
            Task { [weak self] in
                try? await Task.sleep(for: .seconds(3))
                self?.open(from: .urlScheme)
            }
        }
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

    /// The windows of other displays are made ahead, so they follow the Displays settings as they change.
    private func prepareWhenTheDisplaySettingsChange() {
        var last = MultiDisplaySettings(defaults: .standard)
        observers.append(NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                let settings = MultiDisplaySettings(defaults: .standard)
                guard settings != last else { return }
                last = settings
                self?.windowController.prepare()
            }
        })
    }

    /// After sleep or a change of displays the main display may be a different one, so close
    /// instead of staying on a screen that may be gone.
    private func closeWhenTheDisplayMayHaveChanged() {
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.willSleepNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.windowController.close(.displayChanged) }
        })
        // Also posted when the Dock or the menu bar changes size, which a notification can cause
        // (a Handoff tile joins the Dock). Only a change to the displays themselves closes the window.
        observers.append(NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                guard self.displays.shouldClose(on: .current) else {
                    self.log.info("Screen parameters changed, displays unchanged")
                    return
                }
                self.windowController.close(.displayChanged)
                self.windowController.prepare()
            }
        })
    }

    func showSettings() {
        settings.show()
    }

    private func open(from source: TriggerSource, onRefused: (() -> Void)? = nil) {
        // While input is ignored ("Close on input" off, or the debug switch), a second manual trigger closes it.
        if windowController.isOpen {
            if source.isManual && !windowController.closesOnInput { windowController.close(.input) }
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
                onRefused?()
                return
            }
            // Another trigger may have opened it while Spotify was being asked.
            guard !windowController.isOpen else { return }
            windowController.open(source: source)
        }
    }
}
