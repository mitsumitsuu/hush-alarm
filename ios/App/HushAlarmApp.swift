import SwiftUI

@main struct HushAlarmApp: App {
    @StateObject private var model = AlarmModel()
    @Environment(\.scenePhase) private var phase
    var body: some Scene {
        WindowGroup {
            ContentView(model: model)
                .environment(\.locale, Locale(identifier: "ja_JP"))
                .task { await model.unlock() }
                .onChange(of: phase) { _, value in
                    if value == .background { model.lockUI() }
                    if value == .active { Task { await model.unlock(); model.tick() } }
                }
        }
    }
}
