import XCTest
@testable import IdleVizCore

final class AppVersionTests: XCTestCase {
    func testReadsATagAndAPlainVersion() {
        XCTAssertEqual(AppVersion("v0.1.2")?.description, "0.1.2")
        XCTAssertEqual(AppVersion("0.1.2")?.description, "0.1.2")
        XCTAssertEqual(AppVersion(" V2.0.0\n")?.description, "2.0.0")
    }

    func testTrailingZerosDontMatter() {
        XCTAssertEqual(AppVersion("0.1"), AppVersion("0.1.0"))
        XCTAssertEqual(AppVersion("0.1.1.0"), AppVersion("0.1.1"))
        XCTAssertEqual(AppVersion("1")?.description, "1.0.0")
        XCTAssertEqual(AppVersion("0.0.0")?.description, "0.0.0")
    }

    func testComparesNumbersNotText() throws {
        XCTAssertLessThan(try XCTUnwrap(AppVersion("0.1.9")), try XCTUnwrap(AppVersion("0.1.10")))
        XCTAssertLessThan(try XCTUnwrap(AppVersion("0.9.9")), try XCTUnwrap(AppVersion("1.0")))
        XCTAssertLessThan(try XCTUnwrap(AppVersion("0.1")), try XCTUnwrap(AppVersion("0.1.0.1")))
        XCTAssertFalse(try XCTUnwrap(AppVersion("0.1.1")) < XCTUnwrap(AppVersion("0.1.1.0")))
    }

    func testAnythingElseIsNotAVersion() {
        for text in ["", "v", "latest", "0.2.0-beta", "0.2.0 beta", "1..2", ".1", "1.", "1.2.3.4.5", "-1.0", "1.٢", "99999999999.0"] {
            XCTAssertNil(AppVersion(text), text)
        }
    }
}

final class UpdateCheckTests: XCTestCase {
    private var defaults: UserDefaults!
    private let suite = "UpdateCheckTests"
    private let now = Date(timeIntervalSince1970: 1_800_000_000)

    override func setUp() {
        defaults = UserDefaults(suiteName: suite)
        defaults.removePersistentDomain(forName: suite)
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
    }

    func testIsOnUnlessSwitchedOff() {
        XCTAssertTrue(UpdateCheck.isEnabled(in: defaults))
        defaults.set(false, forKey: UpdateCheck.enabledKey)
        XCTAssertFalse(UpdateCheck.isEnabled(in: defaults))
        defaults.set("no", forKey: UpdateCheck.enabledKey)
        XCTAssertTrue(UpdateCheck.isEnabled(in: defaults))
    }

    func testReadsTheTagOfTheLatestRelease() {
        let answer = Data(#"{"tag_name": "v0.1.2", "name": "IdleViz v0.1.2", "draft": false, "prerelease": false, "assets": []}"#.utf8)
        XCTAssertEqual(UpdateCheck.latestVersion(in: answer), AppVersion("0.1.2"))
        XCTAssertEqual(UpdateCheck.latestVersion(in: Data(#"{"tag_name": "v3.0.0"}"#.utf8)), AppVersion("3"))
    }

    func testAnAnswerThatIsNotAReleaseGivesNothing() {
        for answer in [
            "", "not json", "[]", #"{"message": "API rate limit exceeded"}"#, #"{"tag_name": 12}"#,
            #"{"tag_name": "nightly"}"#, #"{"tag_name": "v0.2.0", "prerelease": true}"#, #"{"tag_name": "v0.2.0", "draft": true}"#,
        ] {
            XCTAssertNil(UpdateCheck.latestVersion(in: Data(answer.utf8)), answer)
        }
    }

    func testChecksOnceADay() {
        XCTAssertEqual(UpdateCheck.wait(lastCheck: nil, now: now), 0)
        XCTAssertEqual(UpdateCheck.wait(lastCheck: now, now: now), UpdateCheck.interval)
        XCTAssertEqual(UpdateCheck.wait(lastCheck: now.addingTimeInterval(-3600), now: now), UpdateCheck.interval - 3600)
        XCTAssertEqual(UpdateCheck.wait(lastCheck: now.addingTimeInterval(-UpdateCheck.interval), now: now), 0)
        XCTAssertEqual(UpdateCheck.wait(lastCheck: now.addingTimeInterval(-7 * UpdateCheck.interval), now: now), 0)
    }

    func testALastCheckInTheFutureCountsAsNever() {
        XCTAssertEqual(UpdateCheck.wait(lastCheck: now.addingTimeInterval(60), now: now), 0)
    }

    func testOffersOnlyANewerRelease() {
        let installed = AppVersion("0.1.1")
        XCTAssertEqual(UpdateCheck.available(installed: installed, latest: AppVersion("0.1.2")), AppVersion("0.1.2"))
        XCTAssertNil(UpdateCheck.available(installed: installed, latest: AppVersion("0.1.1")))
        XCTAssertNil(UpdateCheck.available(installed: installed, latest: AppVersion("0.1.0")))
        XCTAssertNil(UpdateCheck.available(installed: installed, latest: nil))
        XCTAssertNil(UpdateCheck.available(installed: nil, latest: AppVersion("9.0")))
    }
}
