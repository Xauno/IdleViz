import Foundation
import IdleVizCore
import Observation
import os

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
            send()
        }
    }
    /// Every preset the page can show, sorted by name. Empty until the page has loaded.
    private(set) var presets: [PresetInfo] = []
    /// The preset that was last on screen, so it can be liked or blocked after closing the visualizer.
    private(set) var lastShown: String?
    /// Presets and plugins the page couldn't load.
    private(set) var pageFailures: [PresetFailure] = []
    /// The custom presets folder.
    let library: PresetLibrary
    /// Called by the Preview buttons in settings with the preset to show in the visualizer.
    @ObservationIgnored var onPreview: ((String) -> Void)?

    @ObservationIgnored private let page: PageView
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private var names: [String: String] = [:]
    /// The preset a preview holds the page on, or nil.
    @ObservationIgnored private var previewed: String?
    @ObservationIgnored private let log = Logger(subsystem: "com.xauno.IdleViz", category: "presets")

    init(page: PageView, library: PresetLibrary, defaults: UserDefaults = .standard) {
        self.page = page
        self.library = library
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
        page.onFailures = { [weak self] failures in self?.pageFailures = failures }
        page.onHung = { [weak library] id in library?.markHung(id) }
        library.onChange = { [weak page] payload in page?.send(customPresets: payload) }
        send()
        library.start()
    }

    /// Everything that failed to load: files that didn't convert, then what the page reported.
    var failures: [PresetFailure] {
        let converted = library.conversionFailures
        let known = Set(converted.map(\.id))
        return converted + pageFailures.filter { !known.contains($0.id) }
    }

    var bundledCount: Int { presets.count { $0.source == "bundled" } }

    func name(for id: String) -> String {
        names[id] ?? PresetInfo.fallbackName(for: id)
    }

    var hasCustomPresets: Bool { presets.contains { $0.source == "custom" } }

    /// Holds the page on one preset, whatever the controls say, until `endPreview`. The stored
    /// controls don't change.
    func preview(_ id: String) {
        previewed = id
        send()
    }

    /// Hands the page back to the stored controls after a preview.
    func endPreview() {
        guard previewed != nil else { return }
        previewed = nil
        send()
    }

    private func send() {
        page.send(presetSettings: settings, script: previewed.map { settings.previewScript(preset: $0) })
    }

    /// Runs the like, skip or block key.
    func perform(_ action: VisualizerAction) {
        if action == .skip {
            page.skipPreset()
            return
        }
        // `lastShown` can be a second behind, so ask the page what is on screen right now.
        page.currentPreset { [weak self] id in
            guard let self, let id else { return }
            if action == .block {
                // Shuffle would leave a blocked preset by itself, but over the whole blend time. Skipping
                // first moves on as fast as the skip key does. Single mode has nowhere to move on to.
                self.page.skipPreset()
                self.settings.setBlocked(id, true)
                self.log.notice("Blocked \(id, privacy: .public)")
                return
            }
            let liked = !self.settings.isFavorite(id)
            self.settings.setFavorite(id, liked)
            self.page.showLike(liked)
        }
    }
}
