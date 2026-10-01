import Foundation
import IdleVizCore

/// Sends the brightness and the overlay switch to the page, at launch and whenever settings change them.
@MainActor
final class DisplayOptions {
    private let page: PageView
    private var brightness: Double
    private var overlay: Bool
    private var observer: NSObjectProtocol?

    init(page: PageView) {
        self.page = page
        brightness = BrightnessSetting.value(in: .standard)
        overlay = OverlaySetting.value(in: .standard)
        page.send(brightness: brightness)
        page.send(overlayEnabled: overlay)
        observer = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.settingsMayHaveChanged() }
        }
    }

    private func settingsMayHaveChanged() {
        let brightness = BrightnessSetting.value(in: .standard)
        if brightness != self.brightness {
            self.brightness = brightness
            page.send(brightness: brightness)
        }
        let overlay = OverlaySetting.value(in: .standard)
        if overlay != self.overlay {
            self.overlay = overlay
            page.send(overlayEnabled: overlay)
        }
    }
}
