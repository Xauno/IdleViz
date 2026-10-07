import IdleVizCore
import SwiftUI

/// The Presets section in settings: mode with the rows for that mode folded into it, the
/// last-shown preset, the favorites and blocklist sheets, and the library.
struct PresetControls: View {
    @Bindable var presets: PresetController
    @State private var sheet: PresetList?
    /// Which rows are open isn't stored: Mode starts open, the others closed.
    @State private var modeOpen = true
    @State private var showPicker = false

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
        LabeledContent("Visualizer") {
            Button {
                showPicker = true
            } label: {
                Label(presets.name(for: settings.single), systemImage: "magnifyingglass")
                    .lineLimit(1)
                    .truncationMode(.tail)
                    .frame(maxWidth: 150)
            }
            .accessibilityLabel("Visualizer: \(presets.name(for: settings.single))")
            .sheet(isPresented: $showPicker) {
                PresetPickerSheet(presets: presets)
            }
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

/// A search box that is always there, with a magnifier in it.
private struct SearchField: View {
    @Binding var text: String

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
            TextField("Search", text: $text).textFieldStyle(.plain)
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 4)
        .background(.quaternary, in: RoundedRectangle(cornerRadius: 6))
    }
}

/// What a list shows when the search matches nothing.
private struct NoMatches: View {
    var body: some View {
        Text("No presets match.")
            .font(.callout)
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// The preset of Single mode: every preset as a list of names with a search box, scrolled to the
/// current one. Picking a row stores it and tells the page at once.
struct PresetPickerSheet: View {
    @Bindable var presets: PresetController
    @Environment(\.dismiss) private var dismiss
    @State private var search = ""

    /// Every preset, with a stored one that is gone from the library still listed first.
    private var all: [PresetInfo] {
        let single = presets.settings.single
        guard !presets.presets.contains(where: { $0.id == single }) else { return presets.presets }
        return [PresetInfo(id: single, name: presets.name(for: single), source: "bundled")] + presets.presets
    }

    var body: some View {
        let matches = PresetSettings.matching(all, search: search)
        VStack(spacing: 10) {
            HStack {
                Text("Visualizer").font(.headline)
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            SearchField(text: $search)
            if matches.isEmpty {
                NoMatches()
            } else {
                ScrollViewReader { proxy in
                    List(matches) { preset in
                        Button {
                            presets.settings.single = preset.id
                        } label: {
                            HStack {
                                Text(preset.name).lineLimit(1)
                                Spacer()
                                if preset.id == presets.settings.single { Image(systemName: "checkmark") }
                            }
                            .contentShape(.rect)
                        }
                        .buttonStyle(.plain)
                        .font(.callout)
                        .id(preset.id)
                    }
                    .onAppear { proxy.scrollTo(presets.settings.single, anchor: .center) }
                }
            }
        }
        .padding(12)
        .frame(width: 320, height: 440)
    }
}

/// Lists the presets on the favorites or the blocklist, where they can be removed, and every
/// preset, where they can be added. Both lists have a search box, and every row a Preview button.
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

    /// The presets of the tab that is showing, filtered by the search.
    private var matches: [PresetInfo] {
        let shown = showAll
            ? presets.presets
            : ids.map { PresetInfo(id: $0, name: presets.name(for: $0), source: $0.hasPrefix("custom:") ? "custom" : "bundled") }
        return PresetSettings.matching(shown, search: search)
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
            SearchField(text: $search)
            if !showAll, ids.isEmpty {
                Text(list.emptyText)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if matches.isEmpty {
                NoMatches()
            } else {
                List(matches) { preset in
                    row(id: preset.id, name: preset.name, custom: preset.source == "custom")
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
                presets.onPreview?(id)
            } label: {
                Image(systemName: "play")
            }
            .buttonStyle(.borderless)
            .help("Preview in the visualizer")
            .accessibilityLabel("Preview \(name)")
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
