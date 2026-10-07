import Foundation
import IdleVizCore

/// Sends the brightness, the overlay switch and the title switch to the page, at launch and whenever settings change them.
@MainActor
final class DisplayOptions {
    private let page: PageView
    private var brightness: Double
    private var overlay: Bool
    private var title: Bool
    private var observer: NSObjectProtocol?

    init(page: PageView) {
        self.page = page
        brightness = BrightnessSetting.value(in: .standard)
        overlay = OverlaySetting.value(in: .standard)
        page.send(brightness: brightness)
        title = PresetTitleSetting.value(in: .standard)
        page.send(overlayEnabled: overlay)
        page.send(presetTitleEnabled: title)
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
        let title = PresetTitleSetting.value(in: .standard)
        if title != self.title {
            self.title = title
            page.send(presetTitleEnabled: title)
        }
    }
}
