import AppKit
import IdleVizCore
import KeyboardShortcuts
import SwiftUI

/// Separate settings window, per mockups.html section 2: one scrolling page with the General,
/// Visualizer and Presets sections.
@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    private let presets: PresetController
    private let audioDelay: AudioDelayController
    private let openNow: () -> Void
    private var window: NSWindow?

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
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 560),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Settings"
        window.contentViewController = hosting
        window.setContentSize(NSSize(width: 340, height: 560))
        window.collectionBehavior = [.fullScreenNone]
        window.standardWindowButton(.zoomButton)?.isEnabled = false
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
        return window
    }
}

struct SettingsView: View {
    @Bindable var presets: PresetController
    @Bindable var audioDelay: AudioDelayController
    let openNow: () -> Void
    @AppStorage(IdleTimeoutSetting.key) private var idleTimeout = IdleTimeoutSetting.defaultMinutes
    @AppStorage(KeepAwakeSetting.key) private var keepAwake = KeepAwakeSetting.defaultMinutes
    @AppStorage(BatteryTimesSetting.enabledKey) private var useBatteryTimes = false
    @AppStorage(BatteryTimesSetting.idleTimeoutKey) private var idleTimeoutBattery = IdleTimeoutSetting.defaultMinutes
    @AppStorage(BatteryTimesSetting.keepAwakeKey) private var keepAwakeBattery = KeepAwakeSetting.defaultMinutes
    @AppStorage(OverlaySetting.key) private var showOverlay = true
    @AppStorage(BrightnessSetting.key) private var brightness = BrightnessSetting.defaultValue

    var body: some View {
        Form {
            Section("General") {
                idlePicker($idleTimeout) {
                    Text("Start after idle")
                    Text("Needs a Spotify track")
                }
                keepAwakePicker($keepAwake) {
                    Text("Keep screen awake")
                    Text("Then the Mac sleeps as usual")
                }
                if PowerSource.hasBattery {
                    Toggle("Different times on battery", isOn: $useBatteryTimes)
                        .onChange(of: useBatteryTimes) { _, enabled in
                            if enabled { copyTimesToBatteryIfUnset() }
                        }
                    if useBatteryTimes {
                        idlePicker($idleTimeoutBattery) { Text("On battery: start after idle") }
                        keepAwakePicker($keepAwakeBattery) { Text("On battery: keep screen awake") }
                    }
                }
                KeyboardShortcuts.Recorder("Open hotkey", name: .openVisualizer)
                LabeledContent("Open now") {
                    Button("Open", action: openNow)
                }
                LaunchAtLoginToggle()
            }
            Section("Visualizer") {
                Toggle("Show Spotify overlay", isOn: $showOverlay)
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
                AudioDelayControls(audioDelay: audioDelay)
                PresetControls(presets: presets)
            }
            PresetFolderControls(presets: presets)
        }
        .formStyle(.grouped)
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
