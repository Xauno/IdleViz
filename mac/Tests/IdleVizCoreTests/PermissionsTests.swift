import XCTest
@testable import IdleVizCore

final class PermissionStatusTests: XCTestCase {
    func testNothingIsMissingWhenBothAreGranted() {
        let status = PermissionStatus(automation: .granted, audio: .granted)
        XCTAssertEqual(status.missing, [])
        XCTAssertFalse(status.needsWelcome)
    }

    func testUnknownIsNotMissing() {
        // At launch with Spotify closed, nothing is known to be wrong yet.
        let status = PermissionStatus()
        XCTAssertEqual(status.missing, [])
        XCTAssertFalse(status.needsWelcome)
    }

    func testDeniedAndNeverAskedAreMissingInRowOrder() {
        let status = PermissionStatus(automation: .notAsked, audio: .denied)
        XCTAssertEqual(status.missing, [.automation, .audio])
        XCTAssertEqual(PermissionStatus(automation: .granted, audio: .denied).missing, [.audio])
    }

    func testOnlyANeverAskedPermissionBringsUpTheWelcomeWindow() {
        XCTAssertTrue(PermissionStatus(automation: .notAsked, audio: .granted).needsWelcome)
        XCTAssertTrue(PermissionStatus(automation: .denied, audio: .notAsked).needsWelcome)
        // macOS asks only once, so the window can do nothing about a denied permission.
        XCTAssertFalse(PermissionStatus(automation: .denied, audio: .denied).needsWelcome)
    }

    func testAnErrorRowAsksOrOpensSystemSettings() {
        let status = PermissionStatus(automation: .notAsked, audio: .denied)
        XCTAssertEqual(status.action(for: .automation), .showWelcome)
        XCTAssertEqual(status.action(for: .audio), .openSettings)
    }

    func testAutomationStatusCodes() {
        XCTAssertEqual(PermissionStatus.automation(status: 0, previous: .unknown), .granted)
        XCTAssertEqual(PermissionStatus.automation(status: -1743, previous: .granted), .denied)
        XCTAssertEqual(PermissionStatus.automation(status: -1744, previous: .unknown), .notAsked)
    }

    func testAutomationKeepsWhatWasKnownWhileSpotifyIsClosed() {
        XCTAssertEqual(PermissionStatus.automation(status: -600, previous: .denied), .denied)
        XCTAssertEqual(PermissionStatus.automation(status: -600, previous: .unknown), .unknown)
    }

    func testAudioPreflightCodes() {
        XCTAssertEqual(PermissionStatus.audio(preflight: 0, tapSuspected: false), .granted)
        XCTAssertEqual(PermissionStatus.audio(preflight: 1, tapSuspected: false), .denied)
        XCTAssertEqual(PermissionStatus.audio(preflight: 2, tapSuspected: false), .notAsked)
    }

    func testMacOSAnswerWinsOverTheSilentTap() {
        // Spotify Connect and a muted Spotify look like a tap without permission.
        XCTAssertEqual(PermissionStatus.audio(preflight: 0, tapSuspected: true), .granted)
    }

    func testTheSilentTapIsTheFallbackWhenMacOSCannotBeAsked() {
        XCTAssertEqual(PermissionStatus.audio(preflight: nil, tapSuspected: true), .denied)
        XCTAssertEqual(PermissionStatus.audio(preflight: nil, tapSuspected: false), .unknown)
        XCTAssertEqual(PermissionStatus.audio(preflight: 7, tapSuspected: false), .unknown)
    }

    func testRowTextAndSettingsPages() {
        XCTAssertEqual(RequiredPermission.automation.errorTitle, "Spotify control not allowed")
        XCTAssertEqual(RequiredPermission.audio.errorTitle, "Spotify audio blocked")
        XCTAssertTrue(RequiredPermission.automation.settingsURL.absoluteString.hasSuffix("Privacy_Automation"))
        XCTAssertTrue(RequiredPermission.audio.settingsURL.absoluteString.hasSuffix("Privacy_ScreenCapture"))
    }
}
