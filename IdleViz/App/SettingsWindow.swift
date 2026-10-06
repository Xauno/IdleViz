import AppKit
import IdleVizCore
import KeyboardShortcuts
import SwiftUI

/// Separate settings window, per mockups.html section 2: one scrolling page with six sections,
/// in which a row that depends on another row is folded into it.
@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    private let presets: PresetController
    private let audioDelay: AudioDelayController
    private let openNow: () -> Void
    private var window: NSWindow?
    private static let width = 340.0
    private static let height = 640.0

    init(presets: PresetController, audioDelay: AudioDelayController, openNow: @escaping () -> Void) {
        self.presets = presets
        self.audioDelay = audioDelay
        self.openNow = openNow
    }

    func show() {
        let window = window ?? makeWindow()
        self.window = window
        RegularWindows.show(window)
    }

    func windowWillClose(_ notification: Notification) {
        if let window { RegularWindows.closed(window) }
    }

    private func makeWindow() -> NSWindow {
        let view = SettingsView(presets: presets, audioDelay: audioDelay, openNow: openNow)
        let hosting = NSHostingController(rootView: view)
        hosting.sizingOptions = []
        var height = Self.height
        #if DEBUG
        // Launch argument `-IdleVizSettingsHeight 1400` makes the window taller, to see more of it at once.
        let wanted = UserDefaults.standard.double(forKey: "IdleVizSettingsHeight")
        if wanted > 0 { height = wanted }
        #endif
        let window = SettingsWindow(
            contentRect: NSRect(x: 0, y: 0, width: Self.width, height: height),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Settings"
        window.contentViewController = hosting
        window.setContentSize(NSSize(width: Self.width, height: height))
        // The width is fixed; the height can be dragged.
        window.contentMinSize = NSSize(width: Self.width, height: 360)
        window.contentMaxSize = NSSize(width: Self.width, height: 10_000)
        window.collectionBehavior = [.fullScreenNone]
        window.standardWindowButton(.zoomButton)?.isEnabled = false
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
        return window
    }
}

/// The settings window. In Debug builds a height given at launch may be taller than the screen,
/// so that a capture of the window shows all of it.
private final class SettingsWindow: NSWindow {
    #if DEBUG
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        UserDefaults.standard.double(forKey: "IdleVizSettingsHeight") > 0 ? frameRect : super.constrainFrameRect(frameRect, to: screen)
    }
    #endif
}

/// A row with the rows that depend on it folded into it. They are inset under their parent.
struct FoldedRow<Label: View, Rows: View>: View {
    @Binding var isOpen: Bool
    @ViewBuilder var rows: Rows
    @ViewBuilder var label: Label

    var body: some View {
        DisclosureGroup(isExpanded: $isOpen) {
            Group { rows }
                .padding(.leading, 18)
                .padding(.top, 6)
        } label: {
            label
        }
    }
}

/// Whether the folded rows start open. They start closed, except Mode.
enum FoldedRows {
    static let startOpen: Bool = {
        #if DEBUG
        // Launch argument `-IdleVizSettingsUnfolded YES` opens every folded row, to see the whole window at once.
        return UserDefaults.standard.bool(forKey: "IdleVizSettingsUnfolded")
        #else
        return false
        #endif
    }()
}

struct SettingsView: View {
    @Bindable var presets: PresetController
    @Bindable var audioDelay: AudioDelayController
    let openNow: () -> Void
    @AppStorage(OverlaySetting.key) private var showOverlay = true
    @AppStorage(BrightnessSetting.key) private var brightness = BrightnessSetting.defaultValue
    @AppStorage(PresetTitleSetting.key) private var showTitle = false
    @State private var overlayOpen = FoldedRows.startOpen

    var body: some View {
        Form {
            // Not a setting, so it sits above the sections.
            Section {
                LabeledContent {
                    Button("Open now", action: openNow).buttonStyle(.borderedProminent)
                } label: {
                    Text("Open the visualizer")
                    Text("Needs a Spotify track")
                }
            }
            Section("Opening") {
                OpeningControls()
            }
            Section("Look") {
                LabeledContent("Brightness") {
                    HStack(spacing: 6) {
                        Slider(value: $brightness, in: BrightnessSetting.range)
                            .frame(width: 96)
                            .accessibilityLabel("Brightness")
                        Text(BrightnessSetting.label(brightness))
                            .font(.callout)
                            .monospacedDigit()
                            .frame(width: 58, alignment: .trailing)
                    }
                }
                FoldedRow(isOpen: $overlayOpen) {
                    OverlayDisplayPicker()
                } label: {
                    Toggle("Show Spotify overlay", isOn: $showOverlay)
                        .onChange(of: showOverlay) { _, enabled in
                            if enabled { overlayOpen = true }
                        }
                }
                Toggle(isOn: $showTitle) {
                    Text("Show visualizer title")
                    Text("Small, in the top left corner")
                }
            }
            Section("Presets") {
                PresetControls(presets: presets)
            }
            Section("Keys") {
                KeyControls()
            }
            Section("Audio sync") {
                AudioDelayControls(audioDelay: audioDelay)
            }
            Section("Displays") {
                DisplayControls()
            }
        }
        .formStyle(.grouped)
    }
}

/// The Opening section: when the visualizer starts by itself, how long it keeps the screen awake,
/// the hotkey, and whether input closes it.
private struct OpeningControls: View {
    @AppStorage(IdleTimeoutSetting.key) private var idleTimeout = IdleTimeoutSetting.defaultMinutes
    @AppStorage(KeepAwakeSetting.key) private var keepAwake = KeepAwakeSetting.defaultMinutes
    @AppStorage(BatteryTimesSetting.enabledKey) private var useBatteryTimes = false
    @AppStorage(BatteryTimesSetting.idleTimeoutKey) private var idleTimeoutBattery = IdleTimeoutSetting.defaultMinutes
    @AppStorage(BatteryTimesSetting.keepAwakeKey) private var keepAwakeBattery = KeepAwakeSetting.defaultMinutes
    @AppStorage(MultiDisplaySettings.closeOnInputKey) private var closeOnInput = true
    @State private var batteryOpen = FoldedRows.startOpen

    var body: some View {
        idlePicker($idleTimeout) {
            Text("Start after idle")
            Text("Needs a Spotify track")
        }
        keepAwakePicker($keepAwake) {
            Text("Keep screen awake")
            Text("Then the Mac sleeps as usual")
        }
        if PowerSource.hasBattery {
            FoldedRow(isOpen: $batteryOpen) {
                Group {
                    idlePicker($idleTimeoutBattery) { Text("Start after idle") }
                        .accessibilityLabel("On battery: start after idle")
                    keepAwakePicker($keepAwakeBattery) { Text("Keep screen awake") }
                        .accessibilityLabel("On battery: keep screen awake")
                }
                .disabled(!useBatteryTimes)
            } label: {
                Toggle("Different times on battery", isOn: $useBatteryTimes)
                    .onChange(of: useBatteryTimes) { _, enabled in
                        guard enabled else { return }
                        copyTimesToBatteryIfUnset()
                        batteryOpen = true
                    }
            }
        }
        KeyboardShortcuts.Recorder("Open hotkey", name: .openVisualizer)
        Toggle(isOn: $closeOnInput) {
            Text("Close on input")
            Text("Off: the hotkey or Open now closes it")
        }
        LaunchAtLoginToggle()
    }

    private func idlePicker(_ minutes: Binding<Int>, @ViewBuilder label: () -> some View) -> some View {
        Picker(selection: minutes) {
            ForEach(IdleTimeoutSetting.choices, id: \.self) { Text("\($0) min").tag($0) }
            Text("Off").tag(0)
        } label: {
            label()
        }
    }

    private func keepAwakePicker(_ minutes: Binding<Int>, @ViewBuilder label: () -> some View) -> some View {
        Picker(selection: minutes) {
            ForEach(KeepAwakeSetting.choices, id: \.self) { Text(KeepAwakeSetting.label(minutes: $0)).tag($0) }
        } label: {
            label()
        }
    }

    /// The first time the switch is turned on, the battery rows start as copies of the rows above.
    private func copyTimesToBatteryIfUnset() {
        let defaults = UserDefaults.standard
        if defaults.object(forKey: BatteryTimesSetting.idleTimeoutKey) == nil { idleTimeoutBattery = idleTimeout }
        if defaults.object(forKey: BatteryTimesSetting.keepAwakeKey) == nil { keepAwakeBattery = keepAwake }
    }
}
