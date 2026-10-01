import Foundation

/// What a key can do while the visualizer is on screen, instead of closing it.
public enum VisualizerAction: String, Sendable, CaseIterable {
    /// Adds the preset on screen to the favorites, or takes it off again.
    case like
    /// Moves on to the next preset. Only shuffle mode has one.
    case skip
}

/// One key the "Like key" and "Skip key" pickers offer. Keys are matched by their position on the
/// keyboard (the virtual key code), so the labels are the ones of a US layout.
public struct VisualizerKey: Sendable, Hashable, Identifiable {
    public let code: UInt16
    public let label: String

    public var id: UInt16 { code }

    /// A–Z, 0–9, the arrow keys and Space, in the order the pickers list them.
    public static let choices: [VisualizerKey] = letters + digits + [
        VisualizerKey(code: 123, label: "←"),
        VisualizerKey(code: 124, label: "→"),
        VisualizerKey(code: 126, label: "↑"),
        VisualizerKey(code: 125, label: "↓"),
        VisualizerKey(code: 49, label: "Space"),
    ]

    private static let letters: [VisualizerKey] = [
        ("A", 0), ("B", 11), ("C", 8), ("D", 2), ("E", 14), ("F", 3), ("G", 5), ("H", 4), ("I", 34),
        ("J", 38), ("K", 40), ("L", 37), ("M", 46), ("N", 45), ("O", 31), ("P", 35), ("Q", 12), ("R", 15),
        ("S", 1), ("T", 17), ("U", 32), ("V", 9), ("W", 13), ("X", 7), ("Y", 16), ("Z", 6),
    ].map { VisualizerKey(code: $0.1, label: $0.0) }

    private static let digits: [VisualizerKey] = [
        ("0", 29), ("1", 18), ("2", 19), ("3", 20), ("4", 21), ("5", 23), ("6", 22), ("7", 26), ("8", 28), ("9", 25),
    ].map { VisualizerKey(code: $0.1, label: $0.0) }
}

/// The "Like key" and "Skip key" settings. Each is stored as a virtual key code, or `off`.
public struct VisualizerKeys: Sendable, Equatable {
    public static let likeKey = "likeKey"
    public static let skipKey = "skipKey"
    /// The stored value for "Off".
    public static let off = -1
    /// L and N.
    public static let defaultLike = 37
    public static let defaultSkip = 45

    public var like: UInt16?
    public var skip: UInt16?

    public init(like: UInt16? = UInt16(defaultLike), skip: UInt16? = UInt16(defaultSkip)) {
        self.like = like
        // One key can't do both. Settings never offers that, so this only happens with hand-edited values.
        self.skip = skip == like ? nil : skip
    }

    /// Reads the stored keys. A value that was never set, or isn't one of the choices, is the default.
    public init(defaults: UserDefaults) {
        self.init(
            like: Self.code(defaults.object(forKey: Self.likeKey), fallback: Self.defaultLike),
            skip: Self.code(defaults.object(forKey: Self.skipKey), fallback: Self.defaultSkip)
        )
    }

    private static func code(_ stored: Any?, fallback: Int) -> UInt16? {
        let value = (stored as? NSNumber)?.intValue ?? fallback
        if value == off { return nil }
        let code = UInt16(exactly: value) ?? UInt16(fallback)
        return VisualizerKey.choices.contains { $0.code == code } ? code : UInt16(fallback)
    }

    /// The key codes that do something other than close the visualizer.
    public var codes: Set<UInt16> {
        Set([like, skip].compactMap { $0 })
    }

    public func action(for code: UInt16) -> VisualizerAction? {
        if code == like { return .like }
        if code == skip { return .skip }
        return nil
    }

    // MARK: Page

    /// The call that asks the page for the next preset.
    public static let skipScript = "window.skipPreset?.()"

    /// The call that shows the heart: filled for a preset that was just liked, an outline for one that was unliked.
    public static func likeScript(liked: Bool) -> String {
        "window.showLike?.(\(liked))"
    }
}
