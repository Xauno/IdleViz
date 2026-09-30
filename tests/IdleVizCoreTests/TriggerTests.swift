import XCTest
@testable import IdleVizCore

final class TriggerTests: XCTestCase {
    func testParsesOpen() throws {
        XCTAssertEqual(URLCommand(url: try XCTUnwrap(URL(string: "idleviz://open"))), .open)
        XCTAssertEqual(URLCommand(url: try XCTUnwrap(URL(string: "IDLEVIZ://OPEN"))), .open)
        XCTAssertEqual(URLCommand(url: try XCTUnwrap(URL(string: "idleviz://open/"))), .open)
    }

    func testRejectsUnknownCommands() throws {
        XCTAssertNil(URLCommand(url: try XCTUnwrap(URL(string: "idleviz://close"))))
        XCTAssertNil(URLCommand(url: try XCTUnwrap(URL(string: "idleviz://"))))
    }

    func testRejectsOtherSchemes() throws {
        XCTAssertNil(URLCommand(url: try XCTUnwrap(URL(string: "idleviz-app://open"))))
        XCTAssertNil(URLCommand(url: try XCTUnwrap(URL(string: "https://open"))))
    }
}
