import Observation

/// The menu-bar icon. Flashes to an alternate symbol when a manual open is refused.
@MainActor
@Observable
final class MenuBarIcon {
    static let normal = "waveform"
    static let alternate = "waveform.slash"

    private(set) var symbol = normal
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
