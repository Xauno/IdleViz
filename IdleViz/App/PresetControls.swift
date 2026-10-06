import IdleVizCore
import SwiftUI

/// The Audio sync section in settings: the delay for the current speakers or headphones, with
/// the slider, Detect and the manual delay test folded into its row.
struct AudioDelayControls: View {
    @Bindable var audioDelay: AudioDelayController
    @State private var showTest = false
    @State private var isOpen = FoldedRows.startOpen

    var body: some View {
        FoldedRow(isOpen: $isOpen) {
            // Under its label, so the slider runs the width of the row: 250 steps need the room.
            VStack(alignment: .leading, spacing: 6) {
                Text("Set by hand")
                Slider(value: $audioDelay.delay, in: AudioDelaySetting.range)
                    .labelsHidden()
                    .frame(maxWidth: .infinity)
                    .accessibilityLabel("Audio delay")
            }
            LabeledContent {
                Button(audioDelay.detecting ? "Listening…" : "Detect") { audioDelay.detect() }
                    .disabled(audioDelay.detecting)
                    .accessibilityLabel("Detect delay")
            } label: {
                Text("Detect with the microphone")
                switch audioDelay.hint {
                case let .text(text):
                    Text(text)
                case .microphoneDenied:
                    // The microphone is optional, so a missing permission only shows here.
                    Link("Microphone access is off. Open System Settings…", destination: AudioDelayController.microphoneSettingsURL)
                        .font(.caption)
                case nil:
                    Text("Listens for a few seconds")
                }
            }
            LabeledContent {
                Button("Start") { showTest = true }
                    .disabled(audioDelay.detecting)
                    .accessibilityLabel("Start the manual delay test")
            } label: {
                Text("Manual delay test")
                Text("Match a flash to a beep, no mic")
            }
            .sheet(isPresented: $showTest) {
                ManualDelaySheet(audioDelay: audioDelay)
            }
        } label: {
            LabeledContent {
                Text(AudioDelaySetting.label(audioDelay.delay)).monospacedDigit()
            } label: {
                Text("Audio delay")
                Text("For \(audioDelay.deviceName)").lineLimit(1)
            }
        }
    }
}

/// The manual delay test: beeps play through the speakers, the panel lights up one audio delay
/// after each, and the slider is dragged until the two land together.
struct ManualDelaySheet: View {
    @Bindable var audioDelay: AudioDelayController
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 10) {
            HStack {
                Text("Manual delay test").font(.headline)
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            // Redrawn every frame; the flash is worked out from the clock the beeps are scheduled on.
            TimelineView(.animation) { _ in
                RoundedRectangle(cornerRadius: 10)
                    .fill(color(for: audioDelay.testFlash))
                    .frame(height: 120)
            }
            .accessibilityHidden(true)
            if let problem = audioDelay.testProblem {
                Text(problem).font(.callout).foregroundStyle(.red)
            }
            Text("Drag until the panel lights up exactly when you hear the beep. Every fourth beep is higher, and its flash is orange.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 6) {
                Button("−10 ms") { audioDelay.delay -= AudioDelaySetting.step }
                Slider(value: $audioDelay.delay, in: AudioDelaySetting.range)
                    .accessibilityLabel("Audio delay")
                Button("+10 ms") { audioDelay.delay += AudioDelaySetting.step }
            }
            HStack {
                Text("For \(audioDelay.deviceName)").lineLimit(1).foregroundStyle(.secondary)
                Spacer()
                Text(AudioDelaySetting.label(audioDelay.delay)).monospacedDigit()
            }
            .font(.callout)
        }
        .padding(14)
        .frame(width: 320)
        .onAppear { audioDelay.startTest() }
        .onDisappear { audioDelay.stopTest() }
    }

    /// Dark between flashes, white for a beep and orange for the marked one. Fixed colours, so the
    /// flash reads the same in light and dark mode.
    private func color(for flash: BeepTest.Flash?) -> Color {
        guard let flash else { return .black }
        return flash.accent ? .orange : .white
    }
}

/// The Presets section in settings: mode with the rows for that mode folded into it, the
/// last-shown preset, the favorites and blocklist sheets, and the library.
struct PresetControls: View {
    @Bindable var presets: PresetController
    @State private var sheet: PresetList?
    /// Which rows are open isn't stored: Mode starts open, the others closed.
    @State private var modeOpen = true

    private var settings: PresetSettings { presets.settings }

    var body: some View {
        FoldedRow(isOpen: $modeOpen) {
            // The two modes are alternatives, so their rows swap instead of greying out.
            if settings.mode == .shuffle {
                shuffleRows
            } else {
                singleRow
            }
        } label: {
            Picker("Mode", selection: $presets.settings.mode) {
                Text("Shuffle").tag(PresetSettings.Mode.shuffle)
                Text("Single").tag(PresetSettings.Mode.single)
            }
        }
        lastShownRow
        LabeledContent("Favorites") {
            Button("Manage (\(settings.favorites.count))") { sheet = .favorites }
        }
        LabeledContent {
            Button("Manage (\(settings.blocked.count))") { sheet = .blocklist }
                .sheet(item: $sheet) { list in
                    PresetListSheet(list: list, presets: presets)
                }
        } label: {
            Text("Blocklist")
            Text("Never shown in Shuffle")
        }
        PresetLibraryControls(presets: presets)
    }

    @ViewBuilder private var shuffleRows: some View {
        Picker(selection: $presets.settings.shuffleFrom) {
            Text("All").tag(PresetSettings.ShuffleSource.all)
            Text("Bundled").tag(PresetSettings.ShuffleSource.bundled)
            Text("Custom").tag(PresetSettings.ShuffleSource.custom)
            Text("Favorites").tag(PresetSettings.ShuffleSource.favorites)
        } label: {
            Text("Shuffle from")
            if let hint = emptySourceHint { Text(hint) }
        }
        Picker("Time per preset", selection: $presets.settings.secondsPerPreset) {
            ForEach(PresetSettings.secondsChoices, id: \.self) { Text(PresetSettings.secondsLabel($0)).tag($0) }
        }
        Picker(selection: $presets.settings.blendSeconds) {
            ForEach(PresetSettings.blendChoices, id: \.self) { Text(Self.blendLabel($0)).tag($0) }
        } label: {
            Text("Blend time")
            Text("The fade between presets")
        }
    }

    /// Shuffle widens an empty choice to all presets; say so instead of silently ignoring the setting.
    private var emptySourceHint: String? {
        switch settings.shuffleFrom {
        case .favorites where settings.favorites.isEmpty: "No favorites yet, so all presets are used"
        case .custom where !presets.hasCustomPresets: "No custom presets, so all presets are used"
        default: nil
        }
    }

    private var singleRow: some View {
        Picker("Visualizer", selection: $presets.settings.single) {
            // Keeps the stored choice selectable while the list is still loading or the preset is gone.
            if !presets.presets.contains(where: { $0.id == settings.single }) {
                Text(presets.name(for: settings.single)).tag(settings.single)
            }
            ForEach(presets.presets) { Text($0.name).tag($0.id) }
        }
    }

    @ViewBuilder private var lastShownRow: some View {
        if let id = presets.lastShown {
            LabeledContent {
                HStack(spacing: 4) {
                    Toggle("Favorite", isOn: Binding { settings.isFavorite(id) } set: { presets.settings.setFavorite(id, $0) })
                        .help(settings.isFavorite(id) ? "Remove from favorites" : "Add to favorites")
                    Toggle("Block", isOn: Binding { settings.isBlocked(id) } set: { presets.settings.setBlocked(id, $0) })
                        .help(settings.isBlocked(id) ? "Remove from the blocklist" : "Add to the blocklist")
                }
                .toggleStyle(.button)
            } label: {
                Text("Last shown")
                Text(presets.name(for: id)).lineLimit(2)
            }
        }
    }

    static func blendLabel(_ seconds: Double) -> String {
        seconds == seconds.rounded() ? "\(Int(seconds)) s" : "\(seconds.formatted(.number.precision(.fractionLength(1)))) s"
    }
}

/// Which list a sheet manages.
enum PresetList: String, Identifiable {
    case favorites, blocklist

    var id: String { rawValue }
    var title: String { self == .favorites ? "Favorites" : "Blocklist" }
    var emptyText: String {
        self == .favorites
            ? "No favorites yet. Add some from All presets, or with the heart next to Last shown."
            : "Nothing blocked. Blocked presets are left out of shuffle."
    }
}

/// Lists the presets on the favorites or the blocklist, where they can be removed, and every
/// preset with a search field, where they can be added.
struct PresetListSheet: View {
    let list: PresetList
    @Bindable var presets: PresetController
    @Environment(\.dismiss) private var dismiss
    @State private var showAll = false
    @State private var search = ""

    private var ids: [String] { list == .favorites ? presets.settings.favorites : presets.settings.blocked }

    private func set(_ id: String, _ listed: Bool) {
        if list == .favorites {
            presets.settings.setFavorite(id, listed)
        } else {
            presets.settings.setBlocked(id, listed)
        }
    }

    private var matches: [PresetInfo] {
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return presets.presets }
        return presets.presets.filter { $0.name.localizedCaseInsensitiveContains(query) }
    }

    var body: some View {
        VStack(spacing: 10) {
            HStack {
                Text(list.title).font(.headline)
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            Picker("Show", selection: $showAll) {
                Text("On the list (\(ids.count))").tag(false)
                Text("All presets").tag(true)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            if showAll {
                TextField("Search", text: $search).textFieldStyle(.roundedBorder)
                List(matches) { preset in
                    row(id: preset.id, name: preset.name, custom: preset.source == "custom")
                }
            } else if ids.isEmpty {
                Text(list.emptyText)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                List(ids, id: \.self) { id in
                    row(id: id, name: presets.name(for: id), custom: id.hasPrefix("custom:"))
                }
            }
        }
        .padding(12)
        .frame(width: 320, height: 440)
    }

    private func row(id: String, name: String, custom: Bool) -> some View {
        let listed = ids.contains(id)
        return HStack {
            VStack(alignment: .leading, spacing: 1) {
                Text(name).lineLimit(2)
                if custom { Text("custom").font(.caption).foregroundStyle(.secondary) }
            }
            Spacer()
            Button {
                set(id, !listed)
            } label: {
                Image(systemName: listed ? "minus.circle.fill" : "plus.circle")
            }
            .buttonStyle(.borderless)
            .help(listed ? "Remove from \(list.title.lowercased())" : "Add to \(list.title.lowercased())")
            .accessibilityLabel(listed ? "Remove \(name)" : "Add \(name)")
        }
        .font(.callout)
    }
}

/// The Library row of the Presets section, with the trust warning, Import, the folder, Reload and
/// what failed to load folded into it.
struct PresetLibraryControls: View {
    @Bindable var presets: PresetController
    @State private var isOpen = FoldedRows.startOpen

    var body: some View {
        FoldedRow(isOpen: $isOpen) {
            // Presets carry equations that run as code, so the warning covers them as well as plugins.
            WarningRow(text: "Custom plugins and presets are code. Only import ones you trust.")
            LabeledContent {
                Button("Import…") { presets.library.importPresets() }
                    .accessibilityLabel("Import presets")
            } label: {
                Text("Import presets")
                Text(".json, .js, .milk")
            }
            LabeledContent {
                Button("Show in Finder") { presets.library.openFolder() }
            } label: {
                Text("Presets folder")
                Text("Your own presets")
            }
            LabeledContent {
                Button("Reload") { presets.library.reload() }
                    .accessibilityLabel("Reload presets")
            } label: {
                Text("Reload presets")
                Text("After changing files in the folder")
            }
            ForEach(presets.failures) { failure in
                LabeledContent {
                    if presets.library.file(for: failure.id) != nil {
                        Button("Reveal") { presets.library.reveal(failure.id) }
                    }
                } label: {
                    Text("Failed to load: \(presets.name(for: failure.id))").lineLimit(1)
                    Text(failure.error).lineLimit(2)
                }
            }
        } label: {
            LabeledContent("Library") {
                Text(summary).font(.callout).multilineTextAlignment(.trailing)
            }
        }
    }

    /// "396 bundled, 0 custom, 2 failed to load". The count is here since the list is folded away.
    private var summary: String {
        let counts = "\(presets.bundledCount) bundled, \(presets.library.customCount) custom"
        return presets.failures.isEmpty ? counts : "\(counts), \(presets.failures.count) failed to load"
    }
}
