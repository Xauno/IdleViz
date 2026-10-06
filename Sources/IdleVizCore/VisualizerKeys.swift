import Foundation

/// What a key can do while the visualizer is on screen, instead of closing it.
public enum VisualizerAction: String, Sendable, CaseIterable {
    /// Adds the preset on screen to the favorites, or takes it off again.
    case like
    /// Moves on to the next preset. Only shuffle mode has one.
    case skip
    /// Adds the preset on screen to the blocklist, and in shuffle mode moves on to the next one.
    case block
}

/// The "Like key", "Skip key" and "Block key" settings. Each is stored as a virtual key code, or
/// `off`. Keys are matched by their position on the keyboard, so they are named as on a US layout.
public struct VisualizerKeys: Sendable, Equatable {
    public static let likeKey = "likeKey"
    public static let skipKey = "skipKey"
    public static let blockKey = "blockKey"
    /// The stored value for no key.
    public static let off = -1
    /// L, N and B.
    public static let defaultLike = 37
    public static let defaultSkip = 45
    public static let defaultBlock = 11

    public var like: UInt16?
    public var skip: UInt16?
    public var block: UInt16?

    public init(like: UInt16? = UInt16(defaultLike), skip: UInt16? = UInt16(defaultSkip), block: UInt16? = UInt16(defaultBlock)) {
        self.like = like
        // One key can't do two things. Settings never records that, so this only happens with hand-edited
        // values, or when an older choice for like or skip is the key a newer action defaults to.
        self.skip = skip == like ? nil : skip
        self.block = block == self.like || block == self.skip ? nil : block
    }

    /// Reads the stored keys. A value that was never set, or can't be one of these keys, is the default.
    public init(defaults: UserDefaults) {
        self.init(
            like: Self.code(defaults.object(forKey: Self.likeKey), fallback: Self.defaultLike),
            skip: Self.code(defaults.object(forKey: Self.skipKey), fallback: Self.defaultSkip),
            block: Self.code(defaults.object(forKey: Self.blockKey), fallback: Self.defaultBlock)
        )
    }

    private static func code(_ stored: Any?, fallback: Int) -> UInt16? {
        code(stored: (stored as? NSNumber)?.intValue ?? fallback, fallback: fallback)
    }

    /// The key a stored number stands for: none for `off`, and the default for a number that can't be one of these keys.
    public static func code(stored value: Int, fallback: Int) -> UInt16? {
        if value == off { return nil }
        return canBe(value) ? UInt16(value) : UInt16(fallback)
    }

    /// The key codes that do something other than close the visualizer.
    public var codes: Set<UInt16> {
        Set([like, skip, block].compactMap { $0 })
    }

    public func action(for code: UInt16) -> VisualizerAction? {
        if code == like { return .like }
        if code == skip { return .skip }
        if code == block { return .block }
        return nil
    }

    // MARK: Recording

    private static let escape = 53
    /// Command, Shift, Caps Lock, Option, Control, their right-hand twins and fn (54 to 63).
    private static let modifiers = 54...63

    /// Whether a key can be one of these keys: any keyboard key but Esc, which cancels the recorder,
    /// and the modifiers, which are keys of their own to the visualizer. The media keys have no key
    /// code on a Mac, so they can't be recorded and keep their own job.
    public static func canBe(_ code: Int) -> Bool {
        (0...127).contains(code) && code != escape && !modifiers.contains(code)
    }

    /// "L", "Space", "F5", or "Not set" for no key. A key with no name here is "Key 110".
    public static func label(_ code: UInt16?) -> String {
        guard let code else { return "Not set" }
        return names[code] ?? "Key \(code)"
    }

    private static let names: [UInt16: String] = {
        var names: [UInt16: String] = [
            36: "Return", 48: "Tab", 49: "Space", 51: "Delete", 117: "Forward Delete", 114: "Help",
            115: "Home", 119: "End", 116: "Page Up", 121: "Page Down",
            123: "←", 124: "→", 125: "↓", 126: "↑",
            27: "-", 24: "=", 33: "[", 30: "]", 42: "\\", 41: ";", 39: "'", 43: ",", 47: ".", 44: "/", 50: "`", 10: "§",
            65: "Num .", 67: "Num *", 69: "Num +", 71: "Clear", 75: "Num /", 76: "Num Enter", 78: "Num -", 81: "Num =",
        ]
        let letters: [(String, UInt16)] = [
            ("A", 0), ("B", 11), ("C", 8), ("D", 2), ("E", 14), ("F", 3), ("G", 5), ("H", 4), ("I", 34),
            ("J", 38), ("K", 40), ("L", 37), ("M", 46), ("N", 45), ("O", 31), ("P", 35), ("Q", 12), ("R", 15),
            ("S", 1), ("T", 17), ("U", 32), ("V", 9), ("W", 13), ("X", 7), ("Y", 16), ("Z", 6),
        ]
        for (name, code) in letters { names[code] = name }
        let digits: [UInt16] = [29, 18, 19, 20, 21, 23, 22, 26, 28, 25]
        let keypad: [UInt16] = [82, 83, 84, 85, 86, 87, 88, 89, 91, 92]
        for digit in 0..<10 {
            names[digits[digit]] = "\(digit)"
            names[keypad[digit]] = "Num \(digit)"
        }
        let functionKeys: [UInt16] = [122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111, 105, 107, 113, 106, 64, 79, 80, 90]
        for (index, code) in functionKeys.enumerated() { names[code] = "F\(index + 1)" }
        return names
    }()

    // MARK: Page

    /// The call that asks the page for the next preset.
    public static let skipScript = "window.skipPreset?.()"

    /// The call that shows the heart: filled for a preset that was just liked, an outline for one that was unliked.
    public static func likeScript(liked: Bool) -> String {
        "window.showLike?.(\(liked))"
    }
}
