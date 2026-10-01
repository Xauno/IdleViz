import AppKit
import IdleVizCore
import os

/// The raw checks. They don't touch app state, so the AppleScript thread can call them too.
enum PermissionChecks {
    /// Asks macOS whether IdleViz may send Apple Events to Spotify. This doesn't launch Spotify;
    /// while it isn't running the answer is `procNotFound`. With `ask`, macOS shows its prompt if
    /// it never asked, and the call waits for the answer, so keep it off the main thread.
    nonisolated static func automation(ask: Bool) -> Int32 {
        let target = NSAppleEventDescriptor(bundleIdentifier: SpotifyInfo.bundleID)
        return AEDeterminePermissionToAutomateTarget(target.aeDesc, typeWildCard, typeWildCard, ask)
    }

    private typealias Preflight = @convention(c) (CFString, CFDictionary?) -> Int32

    /// macOS has no public way to ask about System Audio Recording, so this uses the private
    /// `TCCAccessPreflight` and is nil if a future macOS no longer has it.
    private static let preflight: Preflight? = {
        guard let handle = dlopen("/System/Library/PrivateFrameworks/TCC.framework/TCC", RTLD_LAZY),
              let symbol = dlsym(handle, "TCCAccessPreflight")
        else { return nil }
        return unsafeBitCast(symbol, to: Preflight.self)
    }()

    /// 0 allowed, 1 denied, 2 never asked, or nil when the check isn't available.
    nonisolated static func audioPreflight() -> Int32? {
        preflight?("kTCCServiceAudioCapture" as CFString, nil)
    }
}

/// Keeps track of the two required permissions for the yellow menu-bar icon, the popup's error
/// rows and the welcome window, and runs the welcome window's prompts.
@MainActor
@Observable
final class Permissions {
    /// Posted from the AppleScript thread when a query was held back or refused for lack of permission.
    nonisolated static let automationBlocked = Notification.Name("com.xauno.IdleViz.automationBlocked")

    private(set) var status = PermissionStatus()
    /// The permission whose macOS prompt is up right now.
    private(set) var asking: RequiredPermission?
    /// True while the welcome window's prompts wait for Spotify to be opened.
    private(set) var waitingForSpotify = false
    var isRequesting: Bool { request != nil }

    @ObservationIgnored var spotifyIsRunning: () -> Bool = { false }
    /// Starts and stops the Spotify tap, whose first start makes macOS ask for System Audio Recording.
    @ObservationIgnored var startTap: () -> Void = {}
    @ObservationIgnored var stopTap: () -> Void = {}
    /// Called once macOS has an answer about Automation, so the first query can run.
    @ObservationIgnored var onAutomationAnswered: () -> Void = {}
    @ObservationIgnored var onChange: (PermissionStatus) -> Void = { _ in }
    @ObservationIgnored var showWelcome: () -> Void = {}

    @ObservationIgnored private let log = Logger(subsystem: "com.xauno.IdleViz", category: "permissions")
    @ObservationIgnored private var tapSuspected = false
    @ObservationIgnored private var request: Task<Void, Never>?
    @ObservationIgnored private var observers: [(NotificationCenter, NSObjectProtocol)] = []
    #if DEBUG
    /// Launch argument `-IdleVizFakePermissions automation=notAsked,audio=denied` shows the icon,
    /// rows and welcome window in a state without changing the real permissions.
    @ObservationIgnored private let fake: [RequiredPermission: PermissionState] = {
        var states: [RequiredPermission: PermissionState] = [:]
        for pair in (UserDefaults.standard.string(forKey: "IdleVizFakePermissions") ?? "").split(separator: ",") {
            let parts = pair.split(separator: "=").map(String.init)
            guard parts.count == 2, let permission = RequiredPermission(rawValue: parts[0]),
                  let state = PermissionState(rawValue: parts[1]) else { continue }
            states[permission] = state
        }
        return states
    }()
    #endif

    init() {
        checkAudio()
    }

    /// Re-checks when the app becomes active (back from System Settings), when Spotify launches
    /// (Automation can only be checked while it runs), and when a query was blocked.
    func startObserving() {
        let recheck: @Sendable (Notification) -> Void = { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        let app = NotificationCenter.default
        observers.append((app, app.addObserver(
            forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main, using: recheck
        )))
        observers.append((app, app.addObserver(forName: Self.automationBlocked, object: nil, queue: .main, using: recheck)))
        let workspace = NSWorkspace.shared.notificationCenter
        observers.append((workspace, workspace.addObserver(
            forName: NSWorkspace.didLaunchApplicationNotification, object: nil, queue: .main
        ) { [weak self] note in
            let launched = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            guard launched?.bundleIdentifier == SpotifyInfo.bundleID else { return }
            MainActor.assumeIsolated { self?.refresh() }
        }))
    }

    func refresh() {
        Task { await check() }
    }

    /// Reads both permissions without prompting.
    func check() async {
        checkAudio()
        // Asking about a closed Spotify answers nothing, so what was known stays.
        guard spotifyIsRunning() else { return }
        let result = await Task.detached { PermissionChecks.automation(ask: false) }.value
        set(.automation, PermissionStatus.automation(status: result, previous: status.automation))
    }

    private func checkAudio() {
        set(.audio, PermissionStatus.audio(preflight: PermissionChecks.audioPreflight(), tapSuspected: tapSuspected))
    }

    /// From the tap's health check. Only used when macOS can't be asked directly.
    func tapSuspected(_ suspected: Bool) {
        tapSuspected = suspected
        checkAudio()
    }

    private func set(_ permission: RequiredPermission, _ state: PermissionState) {
        var state = state
        #if DEBUG
        state = fake[permission] ?? state
        #endif
        guard status[permission] != state else { return }
        status[permission] = state
        log.notice("\(permission.rawValue, privacy: .public): \(state.rawValue, privacy: .public)")
        onChange(status)
    }

    /// What clicking an error row in the popup does.
    func resolve(_ permission: RequiredPermission) {
        switch status.action(for: permission) {
        case .showWelcome: showWelcome()
        case .openSettings: NSWorkspace.shared.open(permission.settingsURL)
        }
    }

    // MARK: The welcome window's prompts

    /// Shows the macOS prompt for each permission that was never asked, one after the other.
    /// Both need Spotify running, so this first waits for it.
    func requestMissing() {
        guard request == nil else { return }
        request = Task { [weak self] in
            await self?.runRequest()
            self?.asking = nil
            self?.waitingForSpotify = false
            self?.request = nil
        }
    }

    /// Stops waiting, when the welcome window is closed halfway. A prompt that is already up stays up.
    func cancelRequest() {
        request?.cancel()
    }

    private func runRequest() async {
        while !spotifyIsRunning() {
            waitingForSpotify = true
            try? await Task.sleep(for: .milliseconds(500))
            if Task.isCancelled { return }
        }
        waitingForSpotify = false
        await check()
        if status.automation == .notAsked {
            asking = .automation
            let result = await Task.detached { PermissionChecks.automation(ask: true) }.value
            set(.automation, PermissionStatus.automation(status: result, previous: status.automation))
            onAutomationAnswered()
        }
        if status.audio == .notAsked, !Task.isCancelled {
            asking = .audio
            startTap()
            // The prompt belongs to the tap. Keep the tap up until macOS has an answer, for two minutes at most.
            for _ in 0..<240 where status.audio == .notAsked && !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(500))
                checkAudio()
            }
            stopTap()
        }
    }
}
