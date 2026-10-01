import AppKit
import IdleVizCore
import Observation
import os
import UniformTypeIdentifiers

/// The custom presets folder: scans and watches it, converts `.milk` files once and caches the
/// result, and tells the page what there is to show.
@MainActor
@Observable
final class PresetLibrary {
    /// How many usable presets and plugins the folder holds.
    private(set) var customCount = 0
    /// Files that couldn't be used, with the reason: `.milk` files that didn't convert.
    private(set) var conversionFailures: [PresetFailure] = []

    @ObservationIgnored let folder: URL
    /// Called with the full list each time it changes.
    @ObservationIgnored var onChange: ((CustomPresetPayload) -> Void)?

    @ObservationIgnored private let log = Logger(subsystem: "com.xauno.IdleViz", category: "presets")
    @ObservationIgnored private let converter = MilkConverter()
    @ObservationIgnored private var watcher: FolderWatcher?
    @ObservationIgnored private var pendingScan: Task<Void, Never>?
    @ObservationIgnored private var scanning = false
    @ObservationIgnored private var scanAgain = false
    /// Conversions that failed, by cache key, so the same contents aren't tried on every scan.
    @ObservationIgnored private var failedConversions: [String: String] = [:]
    /// Presets that were on screen when the page stopped answering, with the version that did it.
    @ObservationIgnored private var hung: [String: String] = [:]
    @ObservationIgnored private var versions: [String: String] = [:]
    /// The cache key of each `.milk` file seen so far, with the version it was computed from,
    /// so unchanged files aren't read and hashed again on every scan.
    @ObservationIgnored private var milkKeys: [String: MilkKey] = [:]
    @ObservationIgnored private var payload = CustomPresetPayload()

    init(folder: URL = PresetFolder.defaultURL) {
        self.folder = folder
    }

    /// Creates the folder if needed, reads it, and starts watching it.
    func start() {
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        } catch {
            log.error("Couldn't create the presets folder: \(error.localizedDescription, privacy: .public)")
        }
        watcher = FolderWatcher(url: folder) { [weak self] in self?.folderChanged() }
        reload()
    }

    /// Reads the folder again now.
    func reload() {
        pendingScan?.cancel()
        Task { await scan() }
    }

    /// The page stopped answering while this preset was on screen. It stays out until its file changes.
    func markHung(_ id: String) {
        hung[id] = versions[id] ?? ""
        log.error("Marked as failed after the page stopped answering: \(id, privacy: .public)")
        publish(payload.entries)
    }

    private func folderChanged() {
        // Copying a pack in arrives as many events; wait until they stop.
        pendingScan?.cancel()
        pendingScan = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(300))
            guard !Task.isCancelled else { return }
            await self?.scan()
        }
    }

    // MARK: Scanning

    private struct MilkKey: Sendable {
        var version: String
        var key: String
    }

    private struct Unconverted: Sendable {
        var file: CustomPresetFile
        var key: String
        var source: String
    }

    private struct Scan: Sendable {
        var entries: [CustomPresetPayload.Entry] = []
        var versions: [String: String] = [:]
        /// `.milk` files with no cached conversion yet.
        var unconverted: [Unconverted] = []
        var unreadable: [PresetFailure] = []
        /// The cache key of every `.milk` file, by id.
        var keys: [String: MilkKey] = [:]
    }

    private func scan() async {
        if scanning {
            scanAgain = true
            return
        }
        scanning = true
        defer {
            scanning = false
            if scanAgain {
                scanAgain = false
                Task { await self.scan() }
            }
        }

        let folder = folder
        let bundled = Self.bundledPlugins()
        let known = milkKeys
        var found = await Task.detached(priority: .utility) { Self.read(folder, bundled: bundled, known: known) }.value
        milkKeys = found.keys
        versions = found.versions

        let failures = found.unreadable + (await convertMilkFiles(in: &found))
        Self.removeStaleCache(in: folder, keeping: Set(found.keys.values.map(\.key)))

        // A file that changed since it hung the page gets another chance.
        hung = hung.filter { id, version in !id.hasPrefix("custom:") || found.versions[id] == version }
        conversionFailures = failures
        customCount = found.entries.count { $0.source == "custom" }
        publish(found.entries)
    }

    /// Converts the `.milk` files that have no cached result, adding the ones that work to `scan`.
    /// - Returns: The ones that didn't, with the reason.
    private func convertMilkFiles(in scan: inout Scan) async -> [PresetFailure] {
        var failures: [PresetFailure] = []
        var converted = 0
        for item in scan.unconverted {
            if let message = failedConversions[item.key] {
                failures.append(PresetFailure(id: item.file.id, error: message))
                continue
            }
            switch await converter.convert(item.source) {
            case let .success(json):
                let target = folder.appending(path: MilkConversion.cachePath(forKey: item.key))
                do {
                    let cache = target.deletingLastPathComponent()
                    try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
                    try json.write(to: target, options: .atomic)
                    scan.entries.append(Self.entry(for: item.file, key: item.key))
                    converted += 1
                    // A big pack takes a while; hand over what's ready as it goes.
                    if converted.isMultiple(of: 100) {
                        customCount = scan.entries.count { $0.source == "custom" }
                        publish(scan.entries)
                    }
                } catch {
                    failures.append(PresetFailure(id: item.file.id, error: "Couldn't save the converted preset"))
                }
            case let .failure(error):
                failedConversions[item.key] = error.message
                failures.append(PresetFailure(id: item.file.id, error: error.message))
                log.error("Couldn't convert \(item.file.relativePath, privacy: .public): \(error.message, privacy: .public)")
            }
        }
        converter.close()
        if converted > 0 { log.notice("Converted \(converted, privacy: .public) Milkdrop preset(s)") }
        return failures
    }

    private func publish(_ entries: [CustomPresetPayload.Entry]) {
        let next = CustomPresetPayload(entries: entries, hung: hung.keys.sorted())
        guard next != payload || onChangeNeedsFirstCall else { return }
        onChangeNeedsFirstCall = false
        payload = next
        let failed = conversionFailures.count
        log.notice("Presets folder: \(self.customCount, privacy: .public) custom, \(failed, privacy: .public) not converted")
        onChange?(next)
    }

    @ObservationIgnored private var onChangeNeedsFirstCall = true

    /// Reads the folder off the main thread: every file, plugin names, and which `.milk` files are cached.
    private nonisolated static func read(_ folder: URL, bundled: [CustomPresetPayload.Entry], known: [String: MilkKey]) -> Scan {
        var scan = Scan(entries: bundled)
        for file in PresetFolder.scan(folder) {
            let url = folder.appending(path: file.relativePath)
            scan.versions[file.id] = file.version
            switch file.kind {
            case .json:
                scan.entries.append(CustomPresetPayload.Entry(
                    id: file.id, name: file.name, source: "custom", kind: "preset",
                    url: AppScheme.presetURL(relativePath: file.relativePath), version: file.version
                ))
            case .plugin:
                let name = pluginName(at: url) ?? file.name
                scan.entries.append(CustomPresetPayload.Entry(
                    id: file.id, name: name, source: "custom", kind: "plugin",
                    url: AppScheme.presetURL(relativePath: file.relativePath), version: file.version
                ))
            case .milk:
                let cached = { (key: String) in
                    FileManager.default.fileExists(atPath: folder.appending(path: MilkConversion.cachePath(forKey: key)).path)
                }
                if let before = known[file.id], before.version == file.version, cached(before.key) {
                    scan.keys[file.id] = before
                    scan.entries.append(entry(for: file, key: before.key))
                    continue
                }
                guard let data = try? Data(contentsOf: url) else {
                    scan.unreadable.append(PresetFailure(id: file.id, error: "Couldn't read the file"))
                    continue
                }
                let key = MilkConversion.cacheKey(for: data)
                scan.keys[file.id] = MilkKey(version: file.version, key: key)
                if cached(key) {
                    scan.entries.append(entry(for: file, key: key))
                } else {
                    // Many Milkdrop files predate UTF-8; Latin-1 reads any byte.
                    let source = String(data: data, encoding: .utf8) ?? String(data: data, encoding: .isoLatin1) ?? ""
                    scan.unconverted.append(Unconverted(file: file, key: key, source: source))
                }
            }
        }
        return scan
    }

    private nonisolated static func entry(for file: CustomPresetFile, key: String) -> CustomPresetPayload.Entry {
        CustomPresetPayload.Entry(
            id: file.id, name: file.name, source: "custom", kind: "preset",
            url: AppScheme.presetURL(relativePath: MilkConversion.cachePath(forKey: key)), version: file.version
        )
    }

    /// The name a plugin declares, read from the start of its source.
    private nonisolated static func pluginName(at url: URL) -> String? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        guard let data = try? handle.read(upToCount: 256 * 1024) else { return nil }
        return String(data: data, encoding: .utf8).flatMap(PluginMeta.name(inSource:))
    }

    /// The plugins shipped in the app's `web/visuals/` folder. They run in the same sandboxed frame as custom ones.
    private static func bundledPlugins() -> [CustomPresetPayload.Entry] {
        guard let visuals = Bundle.main.resourceURL?.appending(path: "web/visuals"),
              let files = try? FileManager.default.contentsOfDirectory(at: visuals, includingPropertiesForKeys: nil)
        else { return [] }
        return files.filter { $0.pathExtension == "js" }.sorted { $0.lastPathComponent < $1.lastPathComponent }.map { file in
            let name = pluginName(at: file) ?? file.deletingPathExtension().lastPathComponent
            let path = file.lastPathComponent.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? file.lastPathComponent
            return CustomPresetPayload.Entry(
                id: "bundled:visuals/\(file.lastPathComponent)", name: name, source: "bundled", kind: "plugin",
                url: "\(AppScheme.scheme)://\(AppScheme.appHost)/visuals/\(path)", version: "bundled"
            )
        }
    }

    /// Deletes cached conversions whose `.milk` file is gone or changed.
    private nonisolated static func removeStaleCache(in folder: URL, keeping keys: Set<String>) {
        let cache = folder.appending(path: PresetFolder.cacheFolderName)
        guard let files = try? FileManager.default.contentsOfDirectory(at: cache, includingPropertiesForKeys: nil) else { return }
        for file in files where file.pathExtension == "json" && !keys.contains(file.deletingPathExtension().lastPathComponent) {
            try? FileManager.default.removeItem(at: file)
        }
    }

    // MARK: Settings actions

    /// Asks for files or folders and copies them into the presets folder.
    func importPresets() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        panel.allowedContentTypes = PresetImport.extensions.compactMap { UTType(filenameExtension: $0) }
        panel.message = "Choose presets (.json, .milk), plugins (.js) or folders of them."
        panel.prompt = "Import"
        guard panel.runModal() == .OK else { return }
        var copied = 0
        for source in panel.urls {
            let name = PresetImport.freeName(for: source.lastPathComponent) { candidate in
                FileManager.default.fileExists(atPath: folder.appending(path: candidate).path)
            }
            do {
                try FileManager.default.copyItem(at: source, to: folder.appending(path: name))
                copied += 1
            } catch {
                log.error("Couldn't import \(source.lastPathComponent, privacy: .public): \(error.localizedDescription, privacy: .public)")
            }
        }
        log.notice("Imported \(copied, privacy: .public) item(s)")
        reload()
    }

    func openFolder() {
        NSWorkspace.shared.open(folder)
    }

    /// The file behind a custom preset's id, or nil for a bundled one.
    func file(for id: String) -> URL? {
        guard id.hasPrefix("custom:") else { return nil }
        return folder.appending(path: String(id.dropFirst("custom:".count)))
    }

    func reveal(_ id: String) {
        guard let file = file(for: id) else { return }
        NSWorkspace.shared.activateFileViewerSelecting([file])
    }
}
