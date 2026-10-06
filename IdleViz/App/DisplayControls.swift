import AppKit
import IdleVizCore
import SwiftUI

/// The Displays rows of the settings window: the main display, the switch for more than one
/// display, and the rows that only count while it is on.
struct DisplayControls: View {
    @AppStorage(MultiDisplaySettings.mainDisplayKey) private var mainDisplay = ""
    @AppStorage(MultiDisplaySettings.enabledKey) private var enabled = false
    @AppStorage(MultiDisplaySettings.placementKey) private var placement = DisplayPlacement.mirror.rawValue
    /// The connected displays, macOS's main one first.
    @State private var displays = DisplayLayout.current.displays
    @State private var canSpan = Displays.canSpan
    /// The chosen other displays, or nil for all of them. `@AppStorage` can't hold a list.
    @State private var chosenOthers = UserDefaults.standard.stringArray(forKey: MultiDisplaySettings.otherDisplaysKey)

    var body: some View {
        Picker("Main display", selection: $mainDisplay) {
            Text("macOS main display").tag("")
            DisplayChoices(displays: displays, stored: mainDisplay)
        }
        Toggle(isOn: $enabled) {
            Text("Use more than one display")
            Text("Shows the visualizer on other displays too")
        }
        if enabled {
            WarningRow(text: MultiDisplaySettings.gpuWarning)
        }
        Group {
            LabeledContent("Other displays") {
                Menu(othersSummary) {
                    ForEach(labelled.filter { others.contains($0.display) }, id: \.display.uuid) { item in
                        Toggle(item.label, isOn: covered(item.display))
                    }
                }
                .fixedSize()
                .disabled(others.isEmpty)
            }
            Picker("Placement", selection: $placement) {
                Text("Same on each display").tag(DisplayPlacement.mirror.rawValue)
                Text("Extend across displays").tag(DisplayPlacement.extend.rawValue)
            }
            if enabled, placement == DisplayPlacement.extend.rawValue, !canSpan {
                WarningRow(text: MultiDisplaySettings.separateSpacesWarning)
            }
        }
        .disabled(!enabled)
        // A display may be plugged in or pulled out, or the Spaces setting changed, while the window is open.
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didChangeScreenParametersNotification)) { _ in refresh() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in refresh() }
    }

    private func refresh() {
        displays = DisplayLayout.current.displays
        canSpan = Displays.canSpan
    }

    private var settings: MultiDisplaySettings {
        MultiDisplaySettings(mainDisplay: mainDisplay.isEmpty ? nil : mainDisplay, otherDisplays: chosenOthers)
    }

    private var labelled: [(display: Display, label: String)] {
        Array(zip(displays, DisplayLabel.labels(for: displays)))
    }

    /// Every display but the main one.
    private var others: [Display] {
        let main = settings.main(among: displays)
        return displays.filter { $0 != main }
    }

    private var othersSummary: String {
        let chosen = others.count { settings.covers($0) }
        if others.isEmpty { return "None connected" }
        if chosen == others.count { return "All" }
        return chosen == 0 ? "None" : "\(chosen) of \(others.count) displays"
    }

    private func covered(_ display: Display) -> Binding<Bool> {
        Binding {
            settings.covers(display)
        } set: { isOn in
            var chosen = Set(others.filter { settings.covers($0) }.map(\.uuid))
            if isOn { chosen.insert(display.uuid) } else { chosen.remove(display.uuid) }
            if chosen.count == others.count {
                // All of them, including a display that is plugged in later.
                chosenOthers = nil
                UserDefaults.standard.removeObject(forKey: MultiDisplaySettings.otherDisplaysKey)
            } else {
                chosenOthers = others.map(\.uuid).filter(chosen.contains)
                UserDefaults.standard.set(chosenOthers, forKey: MultiDisplaySettings.otherDisplaysKey)
            }
        }
    }
}

/// Where the Spotify overlay shows while more than one display is covered.
struct OverlayDisplayPicker: View {
    @AppStorage(MultiDisplaySettings.overlayDisplayKey) private var overlayDisplay = MultiDisplaySettings.overlayOnMain
    @AppStorage(MultiDisplaySettings.enabledKey) private var multiDisplay = false
    @AppStorage(OverlaySetting.key) private var showOverlay = true
    @State private var displays = DisplayLayout.current.displays

    var body: some View {
        Picker(selection: $overlayDisplay) {
            Text("Main display").tag(MultiDisplaySettings.overlayOnMain)
            DisplayChoices(displays: displays, stored: isDisplay ? overlayDisplay : "")
            Text("All displays").tag(MultiDisplaySettings.overlayOnAll)
        } label: {
            Text("Show it on")
            // With one display covered the overlay has only one place to be.
            if !multiDisplay { Text("Multi-display only") }
        }
        .accessibilityLabel("Spotify overlay on")
        .disabled(!multiDisplay || !showOverlay)
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didChangeScreenParametersNotification)) { _ in
            displays = DisplayLayout.current.displays
        }
    }

    private var isDisplay: Bool {
        overlayDisplay != MultiDisplaySettings.overlayOnMain && overlayDisplay != MultiDisplaySettings.overlayOnAll
    }
}

/// The connected displays as picker choices, tagged with their UUIDs. A stored display that is
/// unplugged right now stays in the list, so the choice isn't lost.
private struct DisplayChoices: View {
    let displays: [Display]
    let stored: String

    var body: some View {
        ForEach(Array(zip(displays, DisplayLabel.labels(for: displays))), id: \.0.uuid) { display, label in
            Text(label).tag(display.uuid)
        }
        if !stored.isEmpty, !displays.contains(where: { $0.uuid == stored }) {
            Text(DisplayLabel.notConnected).tag(stored)
        }
    }
}

/// A warning inside a settings section: a yellow triangle and a few lines of small text.
struct WarningRow: View {
    let text: String

    var body: some View {
        Label {
            Text(text).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.yellow)
        }
    }
}
