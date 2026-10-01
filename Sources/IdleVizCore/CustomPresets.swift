import CryptoKit
import Foundation

/// One file in the custom presets folder.
public struct CustomPresetFile: Sendable, Equatable {
    public enum Kind: String, Sendable {
        /// A Butterchurn preset.
        case json
        /// A visual plugin (see docs/custom-visualizer.md).
        case plugin = "js"
        /// An original Milkdrop preset, converted before use.
        case milk
    }

    /// Path inside the presets folder, with forward slashes: "pack/Tunnel.milk".
    public var relativePath: String
    public var kind: Kind
    /// Size and modification time. A different value means the file changed, so a failed one is tried again.
    public var version: String

    public init(relativePath: String, kind: Kind, version: String) {
        self.relativePath = relativePath
        self.kind = kind
        self.version = version
    }

    public var id: String { "custom:" + relativePath }

    /// The file name without its extension.
    public var name: String {
        let file = relativePath.split(separator: "/").last.map(String.init) ?? relativePath
        guard let dot = file.lastIndex(of: "."), dot != file.startIndex else { return file }
        return String(file[..<dot])
    }
}

public enum PresetFolder {
    /// More files than this are ignored, so a mistaken import of a huge folder can't stall the app.
    public static let maxFiles = 5000
    public static let cacheFolderName = ".cache"

    /// `~/Library/Application Support/IdleViz/Presets/`
    public static var defaultURL: URL {
        URL.applicationSupportDirectory.appending(path: "IdleViz/Presets", directoryHint: .isDirectory)
    }

    /// Every preset and plugin in the folder and its subfolders, sorted by path.
    /// Hidden files and folders (the conversion cache among them) are skipped.
    public static func scan(_ root: URL) -> [CustomPresetFile] {
        let base = root.standardizedFileURL.resolvingSymlinksInPath().path
        let keys: [URLResourceKey] = [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey]
        guard let enumerator = FileManager.default.enumerator(
            at: root, includingPropertiesForKeys: keys, options: [.skipsHiddenFiles, .skipsPackageDescendants]
        ) else { return [] }
        var files: [CustomPresetFile] = []
        for case let url as URL in enumerator {
            guard let kind = CustomPresetFile.Kind(rawValue: url.pathExtension.lowercased()),
                  let values = try? url.resourceValues(forKeys: Set(keys)), values.isRegularFile == true
            else { continue }
            let path = url.standardizedFileURL.resolvingSymlinksInPath().path
            guard path.hasPrefix(base + "/") else { continue }
            let modified = values.contentModificationDate?.timeIntervalSince1970 ?? 0
            let version = "\(values.fileSize ?? 0)-\(Int(modified * 1000))"
            files.append(CustomPresetFile(relativePath: String(path.dropFirst(base.count + 1)), kind: kind, version: version))
            if files.count >= maxFiles { break }
        }
        return files.sorted { $0.relativePath.localizedStandardCompare($1.relativePath) == .orderedAscending }
    }
}

public enum PluginMeta {
    /// The display name a plugin declares with `export const meta = { name: "…" }`, read from its
    /// source without running it. Nil if there is none, or it isn't a plain string.
    public static func name(inSource source: String) -> String? {
        let pattern = #"export\s+const\s+meta\s*=\s*\{[^}]*?\bname\s*:\s*(?:"([^"\\\n]{1,100})"|'([^'\\\n]{1,100})')"#
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: source, range: NSRange(source.startIndex..., in: source))
        else { return nil }
        for group in 1...2 {
            if let range = Range(match.range(at: group), in: source) {
                let name = source[range].trimmingCharacters(in: .whitespaces)
                return name.isEmpty ? nil : name
            }
        }
        return nil
    }
}

/// What `window.setCustomPresets(json)` receives: every plugin bundled with the app and everything
/// usable in the custom presets folder.
public struct CustomPresetPayload: Sendable, Equatable, Encodable {
    public struct Entry: Sendable, Equatable, Encodable {
        public var id: String
        public var name: String
        /// "bundled" or "custom".
        public var source: String
        /// "preset" (Butterchurn JSON) or "plugin" (a JavaScript module).
        public var kind: String
        public var url: String
        public var version: String

        public init(id: String, name: String, source: String, kind: String, url: String, version: String) {
            self.id = id
            self.name = name
            self.source = source
            self.kind = kind
            self.url = url
            self.version = version
        }
    }

    public var entries: [Entry]
    /// Ids of presets that were on screen when the page stopped answering. The page treats them as failed.
    public var hung: [String]

    public init(entries: [Entry] = [], hung: [String] = []) {
        self.entries = entries
        self.hung = hung
    }

    public var script: String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        let json = (try? encoder.encode(self)).flatMap { String(data: $0, encoding: .utf8) } ?? "null"
        let safe = json
            .replacingOccurrences(of: "\u{2028}", with: "\\u2028")
            .replacingOccurrences(of: "\u{2029}", with: "\\u2029")
        return "window.setCustomPresets?.(\(safe))"
    }
}

public enum PresetImport {
    public static let extensions = ["json", "js", "milk"]

    /// A file name for an imported file that doesn't replace one already in the folder:
    /// "Tunnel.milk", then "Tunnel 2.milk", "Tunnel 3.milk", as Finder does.
    public static func freeName(for name: String, isTaken: (String) -> Bool) -> String {
        guard isTaken(name) else { return name }
        let dot = name.lastIndex(of: ".").flatMap { $0 == name.startIndex ? nil : $0 }
        let stem = dot.map { String(name[..<$0]) } ?? name
        let suffix = dot.map { String(name[$0...]) } ?? ""
        var number = 2
        while isTaken("\(stem) \(number)\(suffix)") { number += 1 }
        return "\(stem) \(number)\(suffix)"
    }
}

/// Converted `.milk` presets are kept in `Presets/.cache/<key>.json`, so each file is converted once.
public enum MilkConversion {
    /// Part of every cache key, so results from another converter version aren't reused.
    public static let converterVersion = "milkdrop-preset-converter 0.1.2"
    public static let maxSourceBytes = 1_000_000
    public static let maxResultBytes = 4_000_000

    /// The cache key for a `.milk` file's contents.
    public static func cacheKey(for contents: Data) -> String {
        var hash = SHA256()
        hash.update(data: Data(converterVersion.utf8))
        hash.update(data: contents)
        return hash.finalize().map { String(format: "%02x", $0) }.joined()
    }

    public static func cachePath(forKey key: String) -> String {
        "\(PresetFolder.cacheFolderName)/\(key).json"
    }

    /// Why a file can't be a Milkdrop preset, or nil if it looks like one. The converter turns
    /// any text into an empty preset without complaining, so this is checked first.
    public static func problem(withSource source: String) -> String? {
        if source.utf8.count > maxSourceBytes { return "File is too large" }
        let lowered = source.lowercased()
        if !lowered.contains("[preset") && !lowered.contains("per_frame_") && !lowered.contains("fdecay=") {
            return "Not a Milkdrop preset"
        }
        return nil
    }

    /// Checks what the converter returned. The page that runs it handles untrusted files, so its
    /// answer is untrusted too.
    /// - Returns: The JSON to cache, or a message saying what is wrong with it.
    public static func checkResult(_ reply: Any?) -> Result<Data, ConversionError> {
        guard let text = reply as? String else { return .failure(.init("The converter returned nothing")) }
        let data = Data(text.utf8)
        guard data.count <= maxResultBytes else { return .failure(.init("The converted preset is too large")) }
        guard let preset = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              preset["baseVals"] is [String: Any], preset["shapes"] is [Any], preset["waves"] is [Any]
        else { return .failure(.init("The converter returned something that isn't a preset")) }
        for shader in ["warp", "comp"] {
            // The converter reports a shader it couldn't translate inside the shader text.
            if let code = preset[shader] as? String, code.contains("parsing failed") {
                return .failure(.init("The \(shader) shader couldn't be converted"))
            }
        }
        return .success(data)
    }

    public struct ConversionError: Error, Equatable, Sendable {
        public var message: String
        public init(_ message: String) { self.message = message }
    }
}
