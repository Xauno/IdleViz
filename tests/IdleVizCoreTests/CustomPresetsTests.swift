import XCTest
@testable import IdleVizCore

final class PresetFolderTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appending(path: "PresetFolderTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    private func write(_ path: String, _ text: String = "{}") throws {
        let file = root.appending(path: path)
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: file)
    }

    func testFindsPresetsPluginsAndMilkFilesInSubfolders() throws {
        try write("b.json")
        try write("Pack/Deep/a.MILK")
        try write("Pack/wave.js")
        let files = PresetFolder.scan(root)
        XCTAssertEqual(files.map(\.relativePath), ["b.json", "Pack/Deep/a.MILK", "Pack/wave.js"])
        XCTAssertEqual(files.map(\.kind), [.json, .milk, .plugin])
        XCTAssertEqual(files.map(\.name), ["b", "a", "wave"])
        XCTAssertEqual(files[0].id, "custom:b.json")
    }

    func testSkipsOtherFilesHiddenFilesAndTheCache() throws {
        try write("readme.txt")
        try write(".hidden.json")
        try write(".cache/abc.json")
        try write("folder.json/inner.txt")
        try write("ok.json")
        XCTAssertEqual(PresetFolder.scan(root).map(\.relativePath), ["ok.json"])
    }

    func testVersionChangesWhenTheFileDoes() throws {
        try write("a.json", "{}")
        let before = PresetFolder.scan(root)[0].version
        try write("a.json", "{ \"baseVals\": {} }")
        XCTAssertNotEqual(PresetFolder.scan(root)[0].version, before)
    }

    func testAMissingFolderIsEmpty() {
        XCTAssertEqual(PresetFolder.scan(root.appending(path: "nope")), [])
    }

    func testSortsLikeFinder() throws {
        try write("Preset 10.json")
        try write("Preset 2.json")
        XCTAssertEqual(PresetFolder.scan(root).map(\.name), ["Preset 2", "Preset 10"])
    }
}

final class PluginMetaTests: XCTestCase {
    func testReadsTheDeclaredName() {
        XCTAssertEqual(PluginMeta.name(inSource: "export const meta = { name: \"Aurora Ring\" };"), "Aurora Ring")
        XCTAssertEqual(PluginMeta.name(inSource: "// x\nexport  const meta={author:'me',\n  name : 'Pulse' }"), "Pulse")
    }

    func testIgnoresAnythingElse() {
        XCTAssertNil(PluginMeta.name(inSource: "export function init() {}"))
        XCTAssertNil(PluginMeta.name(inSource: "export const meta = { name: makeName() };"))
        XCTAssertNil(PluginMeta.name(inSource: "const meta = { name: \"Private\" };"))
        XCTAssertNil(PluginMeta.name(inSource: "export const meta = { name: \"\(String(repeating: "x", count: 101))\" };"))
        XCTAssertNil(PluginMeta.name(inSource: "export const meta = { name: \"  \" };"))
    }
}

final class CustomPresetPayloadTests: XCTestCase {
    func testScript() throws {
        let entry = CustomPresetPayload.Entry(
            id: "custom:it's \"odd\".json", name: "it's \"odd\"\u{2028}", source: "custom", kind: "preset",
            url: "idleviz-app://presets/it's%20%22odd%22.json", version: "12-34"
        )
        let script = CustomPresetPayload(entries: [entry], hung: ["custom:loop.js"]).script
        XCTAssertTrue(script.hasPrefix("window.setCustomPresets?.({"))
        XCTAssertFalse(script.contains("\u{2028}"))
        let json = String(script.dropFirst("window.setCustomPresets?.(".count).dropLast())
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
        XCTAssertEqual(object["hung"] as? [String], ["custom:loop.js"])
        let first = try XCTUnwrap((object["entries"] as? [[String: String]])?.first)
        XCTAssertEqual(first["id"], entry.id)
        XCTAssertEqual(first["name"], entry.name)
        XCTAssertEqual(first["url"], entry.url)
        XCTAssertEqual(first["kind"], "preset")
        XCTAssertEqual(first["version"], "12-34")
    }

    func testEmptyPayload() {
        XCTAssertEqual(CustomPresetPayload().script, "window.setCustomPresets?.({\"entries\":[],\"hung\":[]})")
    }
}

final class PresetImportTests: XCTestCase {
    func testKeepsAFreeName() {
        XCTAssertEqual(PresetImport.freeName(for: "Tunnel.milk") { _ in false }, "Tunnel.milk")
    }

    func testNumbersATakenNameLikeFinder() {
        let taken: Set = ["Tunnel.milk", "Tunnel 2.milk", "Pack", ".hidden"]
        XCTAssertEqual(PresetImport.freeName(for: "Tunnel.milk", isTaken: taken.contains), "Tunnel 3.milk")
        XCTAssertEqual(PresetImport.freeName(for: "Pack", isTaken: taken.contains), "Pack 2")
        XCTAssertEqual(PresetImport.freeName(for: ".hidden", isTaken: taken.contains), ".hidden 2")
    }
}

final class MilkConversionTests: XCTestCase {
    private let preset = #"{"baseVals":{"decay":0.9},"shapes":[],"waves":[],"warp":"shader_body { ret = vec3(0.); }","comp":""}"#

    func testCacheKeyFollowsTheContents() {
        let one = MilkConversion.cacheKey(for: Data("fDecay=0.9".utf8))
        XCTAssertEqual(one, MilkConversion.cacheKey(for: Data("fDecay=0.9".utf8)))
        XCTAssertNotEqual(one, MilkConversion.cacheKey(for: Data("fDecay=0.8".utf8)))
        XCTAssertEqual(one.count, 64)
        XCTAssertTrue(one.allSatisfy { $0.isHexDigit })
        XCTAssertEqual(MilkConversion.cachePath(forKey: "abc"), ".cache/abc.json")
    }

    func testSpotsFilesThatAreNotMilkdropPresets() {
        XCTAssertNil(MilkConversion.problem(withSource: "[preset00]\nfDecay=0.94\nper_frame_1=zoom=1;"))
        XCTAssertNil(MilkConversion.problem(withSource: "MILKDROP_PRESET_VERSION=201\n[Preset00]"))
        XCTAssertEqual(MilkConversion.problem(withSource: "hello world"), "Not a Milkdrop preset")
        XCTAssertEqual(MilkConversion.problem(withSource: ""), "Not a Milkdrop preset")
        let huge = "[preset00]" + String(repeating: "x", count: MilkConversion.maxSourceBytes)
        XCTAssertEqual(MilkConversion.problem(withSource: huge), "File is too large")
    }

    func testAcceptsAConvertedPreset() throws {
        XCTAssertEqual(try MilkConversion.checkResult(preset).get(), Data(preset.utf8))
    }

    func testRejectsAnythingElse() {
        func message(_ reply: Any?) -> String? {
            if case let .failure(error) = MilkConversion.checkResult(reply) { return error.message }
            return nil
        }
        XCTAssertEqual(message(nil), "The converter returned nothing")
        XCTAssertEqual(message(42), "The converter returned nothing")
        XCTAssertEqual(message("not json"), "The converter returned something that isn't a preset")
        XCTAssertEqual(message("[1,2]"), "The converter returned something that isn't a preset")
        XCTAssertEqual(message("{\"baseVals\":{},\"shapes\":[]}"), "The converter returned something that isn't a preset")
        XCTAssertEqual(
            message("{\"baseVals\":{},\"shapes\":[],\"waves\":[],\"warp\":\" shader_body { parsing failed }\"}"),
            "The warp shader couldn't be converted"
        )
        let oversized = String(repeating: " ", count: MilkConversion.maxResultBytes + 1)
        XCTAssertEqual(message(oversized), "The converted preset is too large")
    }
}
