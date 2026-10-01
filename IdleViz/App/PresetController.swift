import Foundation
import IdleVizCore
import Observation

/// The preset controls behind the settings window: keeps them in `UserDefaults`, sends each
/// change to the page, and knows the page's preset list and which preset was last on screen.
@MainActor
@Observable
final class PresetController {
    private static let lastShownKey = "lastShownPreset"

    var settings: PresetSettings {
        didSet {
            guard settings != oldValue else { return }
            settings.save(to: defaults)
            page.send(presetSettings: settings)
        }
    }
    /// Every preset the page can show, sorted by name. Empty until the page has loaded.
    private(set) var presets: [PresetInfo] = []
    /// The preset that was last on screen, so it can be liked or blocked after closing the visualizer.
    private(set) var lastShown: String?

    @ObservationIgnored private let page: PageView
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private var names: [String: String] = [:]

    init(page: PageView, defaults: UserDefaults = .standard) {
        self.page = page
        self.defaults = defaults
        settings = PresetSettings(defaults: defaults)
        lastShown = defaults.string(forKey: Self.lastShownKey)
        page.onPresets = { [weak self] presets in
            self?.presets = presets
            self?.names = Dictionary(presets.map { ($0.id, $0.name) }, uniquingKeysWith: { first, _ in first })
        }
        page.onPresetShown = { [weak self] id in
            guard let self, id != self.lastShown else { return }
            self.lastShown = id
            self.defaults.set(id, forKey: Self.lastShownKey)
        }
        page.send(presetSettings: settings)
    }

    func name(for id: String) -> String {
        names[id] ?? PresetInfo.fallbackName(for: id)
    }

    var hasCustomPresets: Bool { presets.contains { $0.source == "custom" } }
}
