import XCTest
@testable import HushCore
final class AlarmScheduleTests: XCTestCase {
    let now = Date(timeIntervalSince1970: 1_792_094_390)
    func testDurationUsesMonotonicClock() throws {
        let schedule = try AlarmSchedule(target: now.addingTimeInterval(60), timerDuration: 60, now: now, uptime: 100)
        XCTAssertEqual(schedule.remaining(now: now.addingTimeInterval(3600), uptime: 115), 45)
        XCTAssertEqual(schedule.remaining(now: now, uptime: 160), 0)
    }
    func testAbsoluteUsesWallClock() throws {
        let schedule = try AlarmSchedule(target: now.addingTimeInterval(60), timerDuration: nil, now: now, uptime: 100)
        XCTAssertEqual(schedule.remaining(now: now.addingTimeInterval(45), uptime: 100), 15)
    }
    func testNextOccurrenceCrossesMidnight() throws {
        var cal = Calendar(identifier: .gregorian); cal.timeZone = TimeZone(secondsFromGMT: 9 * 3600)!
        let before = cal.date(from: DateComponents(year: 2026, month: 10, day: 15, hour: 23, minute: 59, second: 59))!
        let next = try AlarmSchedule.next(hour: 0, minute: 0, now: before, calendar: cal)
        XCTAssertEqual(next.timeIntervalSince(before), 1)
        XCTAssertEqual(cal.component(.day, from: next), 16)
    }
    func testEqualTimeChoosesTomorrow() throws {
        var cal = Calendar(identifier: .gregorian); cal.timeZone = TimeZone(secondsFromGMT: 0)!
        let date = cal.date(from: DateComponents(year: 2026, month: 10, day: 15, hour: 9))!
        XCTAssertEqual(try AlarmSchedule.next(hour: 9, minute: 0, now: date, calendar: cal).timeIntervalSince(date), 86400)
    }
    func testInvalidSchedulesRejected() {
        XCTAssertThrowsError(try AlarmSchedule(target: now, timerDuration: nil, now: now, uptime: 100))
        XCTAssertThrowsError(try AlarmSchedule(target: now.addingTimeInterval(60), timerDuration: 0, now: now, uptime: 100))
        XCTAssertThrowsError(try AlarmSchedule.next(hour: 24, minute: 0, now: now, calendar: .current))
    }
    func testSpeakerAirplayAndUnknownAlwaysDenied() {
        for kind in ["Speaker", "Receiver", "AirPlay", "HDMI", "CarAudio", "Unknown"] {
            XCTAssertFalse(HeadphoneRoute(uid: "registered", kind: kind).allowed(approvedUID: "registered"))
        }
    }
    func testOnlyExplicitRegisteredRouteAllowed() {
        let route = HeadphoneRoute(uid: "headset-1", kind: "BluetoothA2DPOutput")
        XCTAssertFalse(route.allowed(approvedUID: nil))
        XCTAssertFalse(route.allowed(approvedUID: "headset-2"))
        XCTAssertTrue(route.allowed(approvedUID: "headset-1"))
        XCTAssertFalse(HeadphoneRoute(uid: "", kind: "Headphones").canRegister)
    }
}
