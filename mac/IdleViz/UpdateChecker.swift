import AppKit
import IdleVizCore
import os

/// Asks GitHub once a day whether there is a newer release, for the row in the menu-bar popup and
/// the "Check for updates" row in settings. It only tells: nothing is downloaded or installed.
@MainActor
@Observable
final class UpdateChecker {
    /// What the last check from the settings row came to, shown under the switch.
    enum Result {
        case checking, upToDate, failed
    }

    /// The release to offer, or nil when the running copy is the latest or the check is off.
    private(set) var available: AppVersion?
    private(set) var result: Result?
    let installed = AppVersion(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "")

    @ObservationIgnored private let log = Logger(subsystem: "com.xauno.IdleViz", category: "updates")
    @ObservationIgnored private let defaults = UserDefaults.standard
    @ObservationIgnored private var timer: Task<Void, Never>?
    @ObservationIgnored private var enabled = false
    #if DEBUG
    /// Launch argument `-IdleVizFakeUpdate 9.9.9` offers that version, whatever GitHub says.
    @ObservationIgnored private let fake = AppVersion(UserDefaults.standard.string(forKey: "IdleVizFakeUpdate") ?? "")
    #endif

    /// Shows what the last check found and plans the next one. Also follows the switch in settings.
    func start() {
        apply()
        NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, UpdateCheck.isEnabled(in: self.defaults) != self.enabled else { return }
                self.apply()
            }
        }
    }

    /// The "Check now" button: asks GitHub at once, whenever the last check was.
    func checkNow() {
        guard enabled, result != .checking else { return }
        timer?.cancel()
        timer = Task { [weak self] in await self?.run(showResult: true) }
    }

    func openReleasePage() {
        NSWorkspace.shared.open(UpdateCheck.releasePage)
    }

    private func apply() {
        enabled = UpdateCheck.isEnabled(in: defaults)
        timer?.cancel()
        result = nil
        guard enabled else {
            available = nil
            return
        }
        showStored()
        let lastCheck = (defaults.object(forKey: UpdateCheck.lastCheckKey) as? Double).map(Date.init(timeIntervalSince1970:))
        schedule(after: UpdateCheck.wait(lastCheck: lastCheck, now: Date()))
    }

    private func showStored() {
        var latest = AppVersion(defaults.string(forKey: UpdateCheck.latestKey) ?? "")
        #if DEBUG
        if let fake { latest = fake }
        #endif
        available = UpdateCheck.available(installed: installed, latest: latest)
    }

    private func schedule(after seconds: TimeInterval) {
        timer = Task { [weak self] in
            try? await Task.sleep(for: .seconds(seconds))
            guard !Task.isCancelled else { return }
            await self?.run(showResult: false)
        }
    }

    private func run(showResult: Bool) async {
        if showResult { result = .checking }
        let latest = await Self.fetchLatest()
        guard !Task.isCancelled else { return }
        if let latest {
            defaults.set(Date().timeIntervalSince1970, forKey: UpdateCheck.lastCheckKey)
            defaults.set(latest.description, forKey: UpdateCheck.latestKey)
            showStored()
            let installed = installed?.description ?? "unknown"
            log.notice("Latest release is \(latest.description, privacy: .public), this is \(installed, privacy: .public)")
        } else {
            log.notice("Couldn't ask GitHub for the latest release")
        }
        if showResult { result = latest == nil ? .failed : (available == nil ? .upToDate : nil) }
        schedule(after: latest == nil ? UpdateCheck.retryInterval : UpdateCheck.interval)
    }

    private nonisolated static func fetchLatest() async -> AppVersion? {
        var request = URLRequest(url: UpdateCheck.latestRelease, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 20)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        guard let (data, response) = try? await URLSession.shared.data(for: request),
              (response as? HTTPURLResponse)?.statusCode == 200
        else { return nil }
        return UpdateCheck.latestVersion(in: data)
    }
}
