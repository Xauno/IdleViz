import Foundation

/// One preset or plugin the page knows about, for the pickers in settings.
public struct PresetInfo: Sendable, Equatable, Identifiable {
    public static let maxCount = 5000
    public static let maxTextLength = 300

    public var id: String
    public var name: String
    /// "bundled" or "custom".
    public var source: String

    public init(id: String, name: String, source: String) {
        self.id = id
        self.name = name
        self.source = source
    }

    /// Reads the page's answer to `window.idlevizPresets()`. The page runs preset and plugin
    /// code, so the reply is untrusted: wrong shapes are dropped and strings are cut short.
    public static func list(reply: Any?) -> [PresetInfo] {
        guard let items = reply as? [Any] else { return [] }
        var seen = Set<String>()
        return items.prefix(maxCount).compactMap { item in
            guard let item = item as? [String: Any],
                  let id = item["id"] as? String, !id.isEmpty, id.count <= maxTextLength,
                  let name = item["name"] as? String,
                  seen.insert(id).inserted
            else { return nil }
            let source = item["source"] as? String == "custom" ? "custom" : "bundled"
            return PresetInfo(id: id, name: String(name.prefix(maxTextLength)), source: source)
        }
    }

    /// A readable name for an id whose preset is no longer in the library ("custom:My Preset" → "My Preset").
    public static func fallbackName(for id: String) -> String {
        guard let colon = id.firstIndex(of: ":") else { return id }
        return String(id[id.index(after: colon)...])
    }
}

/// The preset controls from the settings window. Stored in `UserDefaults` and sent to the page,
/// which applies them at once.
public struct PresetSettings: Sendable, Equatable, Encodable {
    public enum Mode: String, Sendable, Encodable, CaseIterable {
        case shuffle, single
    }

    public enum ShuffleSource: String, Sendable, Encodable, CaseIterable {
        case all, bundled, custom, favorites
    }

    public static let secondsChoices = [15, 30, 45, 60, 120, 300]
    public static let blendChoices = [0, 1, 2.7, 5, 8]
    /// A beat-driven preset that followed the music well in the step 2 spike.
    public static let defaultSingle = "bundled:Flexi, martin + geiss - dedicated to the sherwin maxawow"

    public var mode = Mode.shuffle
    /// The preset shown in single mode.
    public var single = PresetSettings.defaultSingle
    public var shuffleFrom = ShuffleSource.all
    public var secondsPerPreset = 30
    public var blendSeconds = 2.7
    public var favorites: [String] = []
    /// Presets left out of shuffle. A preset picked in single mode is shown even if it's on this list.
    public var blocked: [String] = []

    public init() {}

    /// "30 s", "2 min". Whole minutes are shown as minutes.
    public static func secondsLabel(_ seconds: Int) -> String {
        seconds >= 60 && seconds % 60 == 0 ? "\(seconds / 60) min" : "\(seconds) s"
    }

    // MARK: Lists

    public func isFavorite(_ id: String) -> Bool { favorites.contains(id) }
    public func isBlocked(_ id: String) -> Bool { blocked.contains(id) }

    /// Adds or removes a favorite. A preset can't be both liked and blocked, so adding it here unblocks it.
    public mutating func setFavorite(_ id: String, _ listed: Bool) {
        favorites.removeAll { $0 == id }
        guard listed else { return }
        favorites.append(id)
        blocked.removeAll { $0 == id }
    }

    /// Adds or removes a blocked preset. Blocking a favorite removes it from the favorites.
    public mutating func setBlocked(_ id: String, _ listed: Bool) {
        blocked.removeAll { $0 == id }
        guard listed else { return }
        blocked.append(id)
        favorites.removeAll { $0 == id }
    }

    // MARK: UserDefaults

    enum Key {
        static let mode = "visualizerMode"
        static let single = "singlePreset"
        static let shuffleFrom = "shuffleFrom"
        static let secondsPerPreset = "secondsPerPreset"
        static let blendSeconds = "blendSeconds"
        static let favorites = "favoritePresets"
        static let blocked = "blockedPresets"
    }

    /// Reads the stored settings. Missing or unusable values fall back to the defaults.
    public init(defaults: UserDefaults) {
        self.init()
        if let mode = defaults.string(forKey: Key.mode).flatMap(Mode.init(rawValue:)) { self.mode = mode }
        if let single = defaults.string(forKey: Key.single), !single.isEmpty { self.single = single }
        if let source = defaults.string(forKey: Key.shuffleFrom).flatMap(ShuffleSource.init(rawValue:)) { shuffleFrom = source }
        let seconds = defaults.integer(forKey: Key.secondsPerPreset)
        if Self.secondsChoices.contains(seconds) { secondsPerPreset = seconds }
        if defaults.object(forKey: Key.blendSeconds) != nil {
            let blend = defaults.double(forKey: Key.blendSeconds)
            if Self.blendChoices.contains(blend) { blendSeconds = blend }
        }
        favorites = Self.unique(defaults.stringArray(forKey: Key.favorites) ?? [])
        blocked = Self.unique(defaults.stringArray(forKey: Key.blocked) ?? []).filter { !favorites.contains($0) }
    }

    public func save(to defaults: UserDefaults) {
        defaults.set(mode.rawValue, forKey: Key.mode)
        defaults.set(single, forKey: Key.single)
        defaults.set(shuffleFrom.rawValue, forKey: Key.shuffleFrom)
        defaults.set(secondsPerPreset, forKey: Key.secondsPerPreset)
        defaults.set(blendSeconds, forKey: Key.blendSeconds)
        defaults.set(favorites, forKey: Key.favorites)
        defaults.set(blocked, forKey: Key.blocked)
    }

    private static func unique(_ ids: [String]) -> [String] {
        var seen = Set<String>()
        return ids.filter { seen.insert($0).inserted }
    }

    // MARK: Page

    /// The call that hands the settings to the page. `JSONEncoder` escapes quotes and backslashes in
    /// preset names; U+2028/U+2029 are escaped too, since older JavaScript rejects them raw.
    public var script: String { script(for: self) }

    /// The call for a page on another display, which shows whatever the main page shows: the same
    /// settings, but held on that one preset. The page blends to it like to any other change.
    public func followScript(preset: String) -> String {
        var held = self
        held.mode = .single
        held.single = preset
        return script(for: held)
    }

    private func script(for settings: PresetSettings) -> String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let json = (try? encoder.encode(settings)).flatMap { String(data: $0, encoding: .utf8) } ?? "null"
        let safe = json
            .replacingOccurrences(of: "\u{2028}", with: "\\u2028")
            .replacingOccurrences(of: "\u{2029}", with: "\\u2029")
        return "window.setPresetSettings?.(\(safe))"
    }
}
