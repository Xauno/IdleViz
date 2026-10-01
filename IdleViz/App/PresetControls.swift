import IdleVizCore
import SwiftUI

/// The preset rows of the Visualizer section in settings: mode, the rows for that mode,
/// the last-shown preset, and the favorites and blocklist sheets.
struct PresetControls: View {
    @Bindable var presets: PresetController
    @State private var sheet: PresetList?

    private var settings: PresetSettings { presets.settings }

    var body: some View {
        Section("Visualizer") {
            Picker("Mode", selection: $presets.settings.mode) {
                Text("Shuffle").tag(PresetSettings.Mode.shuffle)
                Text("Single").tag(PresetSettings.Mode.single)
            }
            if settings.mode == .shuffle {
                shuffleRows
            } else {
                singleRow
            }
            lastShownRow
            LabeledContent("Favorites") {
                Button("Manage (\(settings.favorites.count))") { sheet = .favorites }
            }
            LabeledContent("Blocklist") {
                Button("Manage (\(settings.blocked.count))") { sheet = .blocklist }
            }
        }
        .sheet(item: $sheet) { list in
            PresetListSheet(list: list, presets: presets)
        }
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
        Picker("Seconds per preset", selection: $presets.settings.secondsPerPreset) {
            ForEach(PresetSettings.secondsChoices, id: \.self) { Text("\($0)").tag($0) }
        }
        Picker("Blend time", selection: $presets.settings.blendSeconds) {
            ForEach(PresetSettings.blendChoices, id: \.self) { Text(Self.blendLabel($0)).tag($0) }
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
                    Button {
                        presets.settings.setFavorite(id, !settings.isFavorite(id))
                    } label: {
                        Image(systemName: settings.isFavorite(id) ? "heart.fill" : "heart")
                    }
                    .help(settings.isFavorite(id) ? "Remove from favorites" : "Add to favorites")
                    .accessibilityLabel(settings.isFavorite(id) ? "Remove from favorites" : "Add to favorites")
                    Button {
                        presets.settings.setBlocked(id, !settings.isBlocked(id))
                    } label: {
                        Image(systemName: settings.isBlocked(id) ? "nosign.app.fill" : "nosign")
                    }
                    .help(settings.isBlocked(id) ? "Remove from the blocklist" : "Add to the blocklist")
                    .accessibilityLabel(settings.isBlocked(id) ? "Remove from the blocklist" : "Add to the blocklist")
                }
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
