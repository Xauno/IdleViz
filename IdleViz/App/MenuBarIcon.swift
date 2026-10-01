import AppKit
import Observation

/// The menu-bar icon. Flashes to an alternate symbol when a manual open is refused, and is
/// yellow while a required permission is missing.
@MainActor
@Observable
final class MenuBarIcon {
    static let normal = "waveform"
    static let alternate = "waveform.slash"

    private(set) var symbol = normal
    /// True while a required permission is missing.
    var warning = false

    /// A template image follows the menu bar's own color. The yellow one is a tinted, non-template copy.
    var image: NSImage {
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: "IdleViz") ?? NSImage()
        guard warning else {
            image.isTemplate = true
            return image
        }
        let tinted = image.withSymbolConfiguration(.init(paletteColors: [.systemYellow])) ?? image
        tinted.isTemplate = false
        return tinted
    }
    @ObservationIgnored private var flashing: Task<Void, Never>?

    /// Shows the alternate symbol 3 times over about 1 s.
    func flash() {
        flashing?.cancel()
        flashing = Task { [weak self] in
            for step in 0..<6 {
                self?.symbol = step.isMultiple(of: 2) ? Self.alternate : Self.normal
                try? await Task.sleep(for: .milliseconds(170))
                if Task.isCancelled { break }
            }
            self?.symbol = Self.normal
        }
    }
}
