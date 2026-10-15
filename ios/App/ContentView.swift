import SwiftUI

struct ContentView: View {
    @ObservedObject var model: AlarmModel
    private let accent = Color(red: 0.07, green: 0.44, blue: 0.42)
    var body: some View {
        NavigationStack {
            if model.unlocked {
                ScrollView {
                    VStack(alignment: .leading, spacing: 22) {
                        Text("HUSH").font(.system(size: 38, weight: .bold, design: .rounded)).foregroundStyle(accent)
                            .accessibilityAddTraits(.isHeader)
                        VStack(alignment: .leading, spacing: 12) {
                            Label(model.routeName, systemImage: "headphones").font(.headline)
                            HStack {
                                Text(model.routeReady ? "登録済み" : "未登録").font(.subheadline).foregroundStyle(.secondary)
                                Spacer()
                                Button("イヤホンを登録") { model.requestRegistration() }.disabled(model.active)
                            }
                        }.padding().background(.background, in: RoundedRectangle(cornerRadius: 14))
                        Picker("モード", selection: $model.mode) {
                            Text("時刻指定").tag(0); Text("仮眠タイマー").tag(1)
                        }.pickerStyle(.segmented).disabled(model.active)
                        if model.mode == 0 {
                            DatePicker("起きたい時刻", selection: $model.clock, displayedComponents: .hourAndMinute)
                                .datePickerStyle(.wheel).labelsHidden().frame(maxWidth: .infinity).disabled(model.active)
                        } else {
                            VStack(spacing: 18) {
                                HStack(alignment: .firstTextBaseline) {
                                    Text("\(model.minutes)").font(.system(size: 56, weight: .semibold, design: .rounded))
                                    Text("分").foregroundStyle(.secondary)
                                }
                                HStack {
                                    ForEach([10, 20, 30, 60], id: \.self) { value in
                                        Button("\(value)分") { model.minutes = value }.buttonStyle(.bordered)
                                    }
                                }
                                Stepper("仮眠時間", value: $model.minutes, in: 1...720).labelsHidden()
                            }.frame(maxWidth: .infinity).disabled(model.active)
                        }
                        VStack(alignment: .leading) {
                            Text("音量  \(Int(model.volume * 100))%")
                            Slider(value: $model.volume, in: 0.01...0.5, step: 0.01).accessibilityLabel("音量")
                            HStack {
                                Picker("アラーム音", selection: $model.pattern) {
                                    Text("パルス").tag(0); Text("ウェーブ").tag(1); Text("チャイム").tag(2)
                                }.labelsHidden()
                                Spacer()
                                Button("試聴") { model.start(preview: true) }.disabled(!model.routeReady)
                            }
                        }.disabled(model.active)
                        Text(model.remaining).font(.system(size: 40, weight: .semibold, design: .monospaced))
                            .frame(maxWidth: .infinity).minimumScaleFactor(0.6)
                        if model.active {
                            Button("停止", role: .destructive) { model.stop() }.buttonStyle(.borderedProminent).controlSize(.large).frame(maxWidth: .infinity)
                            if model.state == .ringing {
                                Button("3分後に再通知") { model.start(snooze: 180) }.buttonStyle(.bordered).controlSize(.large).frame(maxWidth: .infinity)
                            }
                        } else {
                            Button { model.start() } label: { Text("セットする").frame(maxWidth: .infinity) }
                                .buttonStyle(.borderedProminent).controlSize(.large).disabled(!model.routeReady)
                        }
                        Text(model.status).font(.subheadline).foregroundStyle(.secondary).frame(maxWidth: .infinity).multilineTextAlignment(.center)
                    }.padding(24).frame(maxWidth: 580)
                }.background(Color(uiColor: .systemGroupedBackground)).navigationBarHidden(true)
            } else {
                VStack(spacing: 24) {
                    Image(systemName: "headphones").font(.system(size: 48)).foregroundStyle(accent)
                    Text("HUSH Alarm").font(.largeTitle.bold())
                    Button("ログイン") { Task { await model.unlock() } }.buttonStyle(.borderedProminent).controlSize(.large)
                    if model.active { Button("アラームを停止", role: .destructive) { model.stop() }.buttonStyle(.bordered) }
                    Text(model.status).font(.subheadline).foregroundStyle(.secondary).multilineTextAlignment(.center)
                }.padding(24)
            }
        }.tint(accent)
        .alert("この機器はイヤホンですか？", isPresented: $model.registrationRequested) {
            Button("イヤホンとして登録") { model.confirmRegistration() }
            Button("キャンセル", role: .cancel) {}
        } message: { Text(model.registrationName) }
    }
}
