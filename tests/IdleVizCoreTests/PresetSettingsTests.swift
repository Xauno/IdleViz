import XCTest
@testable import IdleVizCore

final class PresetSettingsTests: XCTestCase {
    private var defaults: UserDefaults!
    private let suite = "PresetSettingsTests-\(UUID().uuidString)"

    override func setUp() {
        defaults = UserDefaults(suiteName: suite)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
    }

    func testDefaults() {
        let settings = PresetSettings(defaults: defaults)
        XCTAssertEqual(settings, PresetSettings())
        XCTAssertEqual(settings.mode, .shuffle)
        XCTAssertEqual(settings.shuffleFrom, .all)
        XCTAssertEqual(settings.secondsPerPreset, 30)
        XCTAssertEqual(settings.blendSeconds, 2.7)
        XCTAssertTrue(settings.favorites.isEmpty && settings.blocked.isEmpty)
    }

    func testSavesAndLoads() {
        var settings = PresetSettings()
        settings.mode = .single
        settings.single = "custom:Mine"
        settings.shuffleFrom = .favorites
        settings.secondsPerPreset = 120
        settings.blendSeconds = 0
        settings.favorites = ["bundled:A", "bundled:B"]
        settings.blocked = ["bundled:C"]
        settings.save(to: defaults)
        XCTAssertEqual(PresetSettings(defaults: defaults), settings)
    }

    func testUnusableStoredValuesFallBack() {
        defaults.set("sideways", forKey: "visualizerMode")
        defaults.set("everything", forKey: "shuffleFrom")
        defaults.set(7, forKey: "secondsPerPreset")
        defaults.set(99.5, forKey: "blendSeconds")
        defaults.set("", forKey: "singlePreset")
        defaults.set(["bundled:A", "bundled:A", "bundled:B"], forKey: "favoritePresets")
        defaults.set(["bundled:B", "bundled:C"], forKey: "blockedPresets")
        let settings = PresetSettings(defaults: defaults)
        XCTAssertEqual(settings.mode, .shuffle)
        XCTAssertEqual(settings.shuffleFrom, .all)
        XCTAssertEqual(settings.secondsPerPreset, 30)
        XCTAssertEqual(settings.blendSeconds, 2.7)
        XCTAssertEqual(settings.single, PresetSettings.defaultSingle)
        XCTAssertEqual(settings.favorites, ["bundled:A", "bundled:B"])
        XCTAssertEqual(settings.blocked, ["bundled:C"])
    }

    func testAPresetIsNeverBothFavoriteAndBlocked() {
        var settings = PresetSettings()
        settings.setFavorite("bundled:A", true)
        settings.setFavorite("bundled:A", true)
        XCTAssertEqual(settings.favorites, ["bundled:A"])
        settings.setBlocked("bundled:A", true)
        XCTAssertEqual(settings.favorites, [])
        XCTAssertEqual(settings.blocked, ["bundled:A"])
        settings.setFavorite("bundled:A", true)
        XCTAssertEqual(settings.blocked, [])
        XCTAssertTrue(settings.isFavorite("bundled:A"))
        settings.setFavorite("bundled:A", false)
        XCTAssertFalse(settings.isFavorite("bundled:A") || settings.isBlocked("bundled:A"))
    }

    func testScriptCarriesEverythingAndEscapesNames() throws {
        var settings = PresetSettings()
        settings.mode = .single
        settings.single = "custom:it's \"quoted\" \\ \u{2028}"
        settings.blocked = ["bundled:</script>"]
        let script = settings.script
        XCTAssertTrue(script.hasPrefix("window.setPresetSettings?.({"))
        XCTAssertTrue(script.hasSuffix("})"))
        XCTAssertFalse(script.contains("\u{2028}"))
        let json = String(script.dropFirst("window.setPresetSettings?.(".count).dropLast())
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
        XCTAssertEqual(object["mode"] as? String, "single")
        XCTAssertEqual(object["single"] as? String, settings.single)
        XCTAssertEqual(object["shuffleFrom"] as? String, "all")
        XCTAssertEqual(object["secondsPerPreset"] as? Int, 30)
        XCTAssertEqual(object["blendSeconds"] as? Double, 2.7)
        XCTAssertEqual(object["blocked"] as? [String], ["bundled:</script>"])
        XCTAssertEqual(object["favorites"] as? [String], [])
    }

    func testChoicesIncludeTheDefaults() {
        XCTAssertTrue(PresetSettings.secondsChoices.contains(PresetSettings().secondsPerPreset))
        XCTAssertTrue(PresetSettings.blendChoices.contains(PresetSettings().blendSeconds))
    }
}

final class PresetInfoTests: XCTestCase {
    func testReadsTheList() {
        let reply: [Any] = [
            ["id": "bundled:A", "name": "A", "source": "bundled"],
            ["id": "custom:B", "name": "B", "source": "custom"],
        ]
        XCTAssertEqual(PresetInfo.list(reply: reply), [
            PresetInfo(id: "bundled:A", name: "A", source: "bundled"),
            PresetInfo(id: "custom:B", name: "B", source: "custom"),
        ])
    }

    func testDropsWrongShapesAndRepeats() {
        let long = String(repeating: "n", count: 1000)
        let reply: [Any] = [
            "text", 4, ["name": "no id"], ["id": "", "name": "empty"], ["id": String(repeating: "i", count: 301), "name": "long id"],
            ["id": "bundled:A", "name": long, "source": "elsewhere"],
            ["id": "bundled:A", "name": "again"],
        ]
        let list = PresetInfo.list(reply: reply)
        XCTAssertEqual(list.count, 1)
        XCTAssertEqual(list[0].name.count, PresetInfo.maxTextLength)
        XCTAssertEqual(list[0].source, "bundled")
        XCTAssertEqual(PresetInfo.list(reply: "nope"), [])
        XCTAssertEqual(PresetInfo.list(reply: nil), [])
    }

    func testFallbackName() {
        XCTAssertEqual(PresetInfo.fallbackName(for: "custom:My: Preset"), "My: Preset")
        XCTAssertEqual(PresetInfo.fallbackName(for: "plain"), "plain")
    }

    func testSecondsAreLabelledWithTheirUnit() {
        XCTAssertEqual(PresetSettings.secondsChoices.map(PresetSettings.secondsLabel), ["15 s", "30 s", "45 s", "1 min", "2 min", "5 min"])
        XCTAssertEqual(PresetSettings.secondsLabel(90), "90 s")
    }
}
