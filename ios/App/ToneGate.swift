import Foundation
import AVFAudio

// All audio is synthesized locally. A route observer mutes this gate synchronously.
final class ToneGate: @unchecked Sendable {
    private let lock = NSLock()
    private var enabled = false
    private var schedule: AlarmSchedule?
    private var gain = 0.25
    private var sound = 0
    private var limit: TimeInterval = 600
    private var phase: Double = 0
    private var firstRing: TimeInterval?
    private(set) var expired = false
    func arm(_ plan: AlarmSchedule, volume: Double, pattern: Int, maximum: TimeInterval = 600) {
        lock.lock(); defer { lock.unlock() }
        schedule = plan; gain = min(0.5, max(0.01, volume)); sound = pattern
        firstRing = nil; phase = 0; limit = maximum; expired = false; enabled = true
    }
    func mute() { lock.lock(); enabled = false; lock.unlock() }
    var isArmed: Bool { lock.lock(); defer { lock.unlock() }; return enabled }
    var isRinging: Bool { lock.lock(); defer { lock.unlock() }; return enabled && firstRing != nil }
    var isExpired: Bool { lock.lock(); defer { lock.unlock() }; return expired }
    func fill(_ buffers: UnsafeMutableAudioBufferListPointer, frames: Int, sampleRate: Double) {
        let now = Date(), uptime = ProcessInfo.processInfo.systemUptime
        lock.lock(); defer { lock.unlock() }
        var elapsed: Double?
        if enabled, let plan = schedule {
            let left = plan.signedRemaining(now: now, uptime: uptime)
            if left <= 0 {
                if firstRing == nil && left < -90 { enabled = false; expired = true }
                else {
                    if firstRing == nil { firstRing = uptime }
                    let time = uptime - (firstRing ?? uptime)
                    if time < limit { elapsed = time } else { enabled = false; expired = true }
                }
            }
        }
        for index in 0..<frames {
            var value: Float = 0
            if let start = elapsed {
                let time = start + Double(index) / sampleRate, cycle = time.truncatingRemainder(dividingBy: 1.6)
                let local = cycle.truncatingRemainder(dividingBy: 0.4)
                let frequency = sound == 1 ? 660 + 100 * sin(time * 4) : sound == 2 ? (cycle < 0.25 ? 523.25 : cycle < 0.5 ? 659.25 : 783.99) : 740
                let envelope = cycle < 1.2 ? min(1, local / 0.015) * min(1, max(0, (0.32 - local) / 0.04)) : 0
                value = Float(sin(phase) * gain * envelope * min(1, time / 5))
                phase += 2 * .pi * frequency / sampleRate
                if phase > 2 * .pi { phase -= 2 * .pi }
            }
            for buffer in buffers {
                if let data = buffer.mData?.assumingMemoryBound(to: Float.self) {
                    let channels = Int(buffer.mNumberChannels)
                    for channel in 0..<channels { data[index * channels + channel] = value }
                }
            }
        }
    }
}
