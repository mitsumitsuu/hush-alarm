import SwiftUI
import AVFAudio
import Combine
import LocalAuthentication
import MediaPlayer
import UIKit

@MainActor final class AlarmModel: ObservableObject {
    enum State { case idle, armed, ringing, stopped }
    @Published var unlocked = false
    @Published var state: State = .idle
    @Published var mode = 0
    @Published var clock = Date().addingTimeInterval(1200)
    @Published var minutes = 20
    @Published var volume = 0.25
    @Published var pattern = 0
    @Published var status = "イヤホンを接続してください"
    @Published var remaining = "--:--:--"
    @Published var routeName = "未接続"
    @Published var routeReady = false
    @Published var canRegister = false
    @Published var registrationRequested = false
    @Published var registrationName = ""
    private var candidateUID: String?
    private var approvedUID: String? = UserDefaults.standard.string(forKey: "approvedHeadphoneUID")
    private var engine: AVAudioEngine?
    private let gate = ToneGate()
    private var schedule: AlarmSchedule?
    private var observers: [NSObjectProtocol] = []
    private var timer: Timer?
    private var authenticating = false
    private var previewing = false
    var active: Bool { state == .armed || state == .ringing }

    init() {
        let gate = self.gate
        for name in [AVAudioSession.routeChangeNotification, AVAudioSession.interruptionNotification,
                     AVAudioSession.mediaServicesWereResetNotification, .AVAudioEngineConfigurationChange,
                     UIApplication.significantTimeChangeNotification] {
            observers.append(NotificationCenter.default.addObserver(forName: name, object: nil, queue: nil) { [weak self] _ in
                // Mute before dispatching UI work, including when the screen is locked.
                if gate.isArmed {
                    gate.mute()
                    Task { @MainActor [weak self] in self?.safetyStop("接続・再生状態が変わったため停止しました") }
                } else { Task { @MainActor [weak self] in self?.refreshRoute() } }
            })
        }
        timer = Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        MPRemoteCommandCenter.shared().stopCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.stop() }; return .success
        }
        MPRemoteCommandCenter.shared().playCommand.isEnabled = false
        MPRemoteCommandCenter.shared().pauseCommand.isEnabled = false
        MPRemoteCommandCenter.shared().nextTrackCommand.isEnabled = false
        MPRemoteCommandCenter.shared().previousTrackCommand.isEnabled = false
        refreshRoute()
    }
    func unlock() async {
        guard !unlocked, !authenticating else { return }
        authenticating = true; defer { authenticating = false }
        let context = LAContext()
        do {
            guard try await context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: "アラームの設定を開く") else { return }
            unlocked = true; refreshRoute()
        } catch { status = "端末の認証でログインしてください" }
    }
    func lockUI() { unlocked = false }
    private func prepareSession() throws {
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playback, mode: .default, options: [.mixWithOthers])
        try session.setPrefersInterruptionOnRouteDisconnect(true)
        try session.setActive(true)
    }
    private func route() -> (HeadphoneRoute, String)? {
        let outputs = AVAudioSession.sharedInstance().currentRoute.outputs
        guard outputs.count == 1, let port = outputs.first else { return nil }
        return (HeadphoneRoute(uid: port.uid, kind: port.portType.rawValue), port.portName)
    }
    private func allowedRoute() -> Bool { route()?.0.allowed(approvedUID: approvedUID) == true }
    func refreshRoute() {
        let current = route()
        canRegister = current?.0.canRegister == true
        routeReady = current?.0.allowed(approvedUID: approvedUID) == true
        routeName = canRegister ? (current?.1 ?? "未接続") : "未接続"
    }
    func requestRegistration() {
        guard unlocked, !active else { return }
        do {
            try prepareSession(); refreshRoute()
            guard let current = route(), current.0.canRegister else { status = "イヤホンを接続してください"; return }
            candidateUID = current.0.uid; registrationName = current.1; registrationRequested = true
        } catch { status = "イヤホンを確認できませんでした" }
    }
    func confirmRegistration() {
        guard unlocked, let candidate = candidateUID, let current = route(), current.0.uid == candidate, current.0.canRegister else {
            status = "接続先が変わりました。登録し直してください"; return
        }
        approvedUID = candidate; UserDefaults.standard.set(candidate, forKey: "approvedHeadphoneUID")
        candidateUID = nil; refreshRoute(); status = "イヤホンを登録しました"
    }
    func start(snooze: TimeInterval? = nil, preview: Bool = false) {
        guard unlocked, !active || snooze != nil else { return }
        stopAudio()
        do {
            try prepareSession(); refreshRoute()
            guard allowedRoute() else { safetyStop("登録したイヤホンを接続してください"); return }
            let now = Date(), uptime = ProcessInfo.processInfo.systemUptime
            let duration: TimeInterval? = preview ? 0.1 : snooze ?? (mode == 1 ? Double(minutes * 60) : nil)
            let components = Calendar.current.dateComponents([.hour, .minute], from: clock)
            let target = try duration.map { now.addingTimeInterval($0) } ?? AlarmSchedule.next(hour: components.hour ?? 0, minute: components.minute ?? 0, now: now, calendar: .current)
            let plan = try AlarmSchedule(target: target, timerDuration: duration, now: now, uptime: uptime)
            let audioEngine = AVAudioEngine()
            let format = AVAudioFormat(standardFormatWithSampleRate: AVAudioSession.sharedInstance().sampleRate, channels: 2)!
            let renderGate = gate
            let source = AVAudioSourceNode(format: format) { _, _, count, list -> OSStatus in
                renderGate.fill(UnsafeMutableAudioBufferListPointer(list), frames: Int(count), sampleRate: format.sampleRate)
                return noErr
            }
            audioEngine.attach(source); audioEngine.connect(source, to: audioEngine.mainMixerNode, format: format)
            audioEngine.prepare(); try audioEngine.start(); engine = audioEngine
            guard allowedRoute() else { safetyStop("接続先が変わったため停止しました"); return }
            schedule = plan; previewing = preview; state = .armed
            gate.arm(plan, volume: volume, pattern: pattern, maximum: preview ? 4 : 600)
            MPNowPlayingInfoCenter.default().nowPlayingInfo = [
                MPMediaItemPropertyTitle: preview ? "HUSH Alarm — 試聴" : "HUSH Alarm",
                MPMediaItemPropertyArtist: "イヤホン専用",
                MPMediaItemPropertyPlaybackDuration: plan.target.timeIntervalSince(now) + (preview ? 4 : 600),
                MPNowPlayingInfoPropertyElapsedPlaybackTime: 0,
                MPNowPlayingInfoPropertyPlaybackRate: 1
            ]
            MPRemoteCommandCenter.shared().stopCommand.isEnabled = true
            status = preview ? "試聴中" : target.formatted(.dateTime.month().day().hour().minute()) + " に鳴ります"
            tick()
        } catch { safetyStop("再生を開始できませんでした") }
    }
    func tick() {
        refreshRoute()
        guard active else { return }
        guard allowedRoute() else { safetyStop("イヤホンが切断されたため停止しました"); return }
        if gate.isExpired { stop(); status = previewing ? "試聴終了" : "アラームを終了しました"; return }
        if gate.isRinging { state = .ringing; if !previewing { status = "起きる時間です" } }
        if let plan = schedule {
            let seconds = Int(ceil(plan.remaining(now: Date(), uptime: ProcessInfo.processInfo.systemUptime)))
            remaining = String(format: "%02d:%02d:%02d", seconds / 3600, (seconds / 60) % 60, seconds % 60)
        }
    }
    func stop() { stopAudio(); state = .stopped; remaining = "--:--:--"; status = "停止しました" }
    func safetyStop(_ message: String) { stop(); status = message }
    private func stopAudio() {
        gate.mute(); engine?.stop(); engine = nil; schedule = nil
        MPNowPlayingInfoCenter.default().nowPlayingInfo = nil
        MPRemoteCommandCenter.shared().stopCommand.isEnabled = false
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }
}
