import XCTest
@testable import IdleVizCore

final class BeepTestTests: XCTestCase {
    func testThePanelLightsOneDelayAfterEachBeep() {
        // Beeps at 10, 11, 12… with a delay of 0.25 s: lit from 10.25 for 0.12 s.
        XCTAssertNil(BeepTest.flash(at: 10.2, start: 10, delay: 0.25))
        XCTAssertNotNil(BeepTest.flash(at: 10.25, start: 10, delay: 0.25))
        XCTAssertNotNil(BeepTest.flash(at: 10.36, start: 10, delay: 0.25))
        XCTAssertNil(BeepTest.flash(at: 10.38, start: 10, delay: 0.25))
        XCTAssertNotNil(BeepTest.flash(at: 11.3, start: 10, delay: 0.25))
        XCTAssertNil(BeepTest.flash(at: 11.9, start: 10, delay: 0.25))
    }

    func testWithNoDelayThePanelLightsWithTheBeep() {
        XCTAssertNotNil(BeepTest.flash(at: 5, start: 5, delay: 0))
        XCTAssertNil(BeepTest.flash(at: 4.99, start: 5, delay: 0))
    }

    func testNothingLightsBeforeTheFirstBeepIsDue() {
        XCTAssertNil(BeepTest.flash(at: 9, start: 10, delay: 0))
        // The first beep's flash is still 2 s away, though later beeps have sounded.
        XCTAssertNil(BeepTest.flash(at: 11.05, start: 10, delay: 2))
    }

    func testEveryFourthFlashIsMarked() {
        let accents = (0..<9).map { BeepTest.flash(at: 10.05 + Double($0), start: 10, delay: 0)?.accent }
        XCTAssertEqual(accents, [true, false, false, false, true, false, false, false, true])
    }

    func testAFlashKeepsItsBeepsMarkWithALongDelay() {
        // With a 2.3 s delay the flash at 12.35 belongs to beep 0, the marked one, not to beep 2.
        XCTAssertEqual(BeepTest.flash(at: 12.35, start: 10, delay: 2.3), BeepTest.Flash(accent: true))
        XCTAssertEqual(BeepTest.flash(at: 13.35, start: 10, delay: 2.3), BeepTest.Flash(accent: false))
        XCTAssertEqual(BeepTest.flash(at: 16.35, start: 10, delay: 2.3), BeepTest.Flash(accent: true))
    }

    func testBeepTimes() {
        XCTAssertEqual(BeepTest.beepTime(0, start: 10), 10)
        XCTAssertEqual(BeepTest.beepTime(7, start: 10), 17)
        XCTAssertTrue(BeepTest.isAccent(0))
        XCTAssertFalse(BeepTest.isAccent(3))
        XCTAssertTrue(BeepTest.isAccent(8))
    }

    func testABeepIsAShortToneThatStartsAndEndsSilent() {
        let samples = BeepTest.samples(accent: false, sampleRate: 48_000)
        XCTAssertEqual(samples.count, 2880)
        XCTAssertEqual(samples.first, 0)
        XCTAssertEqual(samples.last ?? 1, 0, accuracy: 0.001)
        let peak = samples.map(abs).max() ?? 0
        XCTAssertEqual(peak, 0.4, accuracy: 0.01)
        XCTAssertTrue(BeepTest.samples(accent: true, sampleRate: 0).isEmpty)
    }

    func testTheMarkedBeepIsAnOctaveHigher() {
        func crossings(_ samples: [Float]) -> Int {
            zip(samples, samples.dropFirst()).count { $0 < 0 && $1 >= 0 }
        }
        let low = crossings(BeepTest.samples(accent: false, sampleRate: 48_000))
        let high = crossings(BeepTest.samples(accent: true, sampleRate: 48_000))
        // 880 Hz and 1760 Hz for 60 ms.
        XCTAssertEqual(Double(low), 52.8, accuracy: 1.5)
        XCTAssertEqual(Double(high), 105.6, accuracy: 1.5)
    }
}
