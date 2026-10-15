import Foundation

public struct AlarmSchedule {
    public let target: Date
    public let uptimeDeadline: TimeInterval?
    public init(target: Date, timerDuration: TimeInterval?, now: Date, uptime: TimeInterval) throws {
        guard target > now, target.timeIntervalSince(now) <= 172_800 else { throw ScheduleError.invalidTime }
        if let duration = timerDuration {
            guard duration > 0, duration <= 43_200 else { throw ScheduleError.invalidTime }
            uptimeDeadline = uptime + duration
        } else { uptimeDeadline = nil }
        self.target = target
    }
    public func remaining(now: Date, uptime: TimeInterval) -> TimeInterval {
        max(0, signedRemaining(now: now, uptime: uptime))
    }
    public func signedRemaining(now: Date, uptime: TimeInterval) -> TimeInterval {
        if let deadline = uptimeDeadline { return deadline - uptime }
        return target.timeIntervalSince(now)
    }
    public static func next(hour: Int, minute: Int, now: Date, calendar: Calendar) throws -> Date {
        guard (0...23).contains(hour), (0...59).contains(minute),
              let next = calendar.nextDate(after: now, matching: DateComponents(hour: hour, minute: minute, second: 0), matchingPolicy: .nextTime) else { throw ScheduleError.invalidTime }
        return next
    }
}
public enum ScheduleError: Error { case invalidTime }
public struct HeadphoneRoute {
    public let uid: String
    public let kind: String
    public init(uid: String, kind: String) { self.uid = uid; self.kind = kind }
    public var canRegister: Bool {
        !uid.isEmpty && ["Headphones", "BluetoothA2DPOutput", "BluetoothHFP", "BluetoothLE"].contains(kind)
    }
    public func allowed(approvedUID: String?) -> Bool { canRegister && approvedUID == uid }
}
