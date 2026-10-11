import SwiftUI
import AppKit

final class DashboardState: ObservableObject {
    @Published var localIP = UserDefaults.standard.string(forKey: "localIP") ?? ""
    @Published var pcIP = UserDefaults.standard.string(forKey: "pcIP") ?? ""
    @Published var automatic = UserDefaults.standard.object(forKey: "automatic") as? Bool ?? true
    @Published var autoStart = UserDefaults.standard.object(forKey: "autoStart") as? Bool ?? true
    @Published var muteMac = UserDefaults.standard.object(forKey: "muteMac") as? Bool ?? true
    @Published var audioHint = "Динамики Mac работают обычно."
    @Published var keepAwakeEnabled = UserDefaults.standard.object(forKey: "keepAwake") as? Bool ?? true
    @Published var keepAwakeActive = false
    @Published var keepAwakeHint = "Mac и экран не засыпают, пока EdgeLink запущен."
    @Published var ethernet: [EthernetAddress] = []
    @Published var selectedEthernet = ""
    @Published var displays: [(UInt32, String)] = []
    @Published var selectedDisplay: UInt32 = 0
    @Published var inputAllowed = false
    @Published var audioAllowed = false
    @Published var checkingPermissions = false
    @Published var permissionHint = "Включите EdgeLink в системных настройках. После подтверждения вернитесь сюда."
    @Published var running = false
    @Published var connected = false
    @Published var busy = false
    @Published var status = "Подключите компьютеры Ethernet-кабелем"
    @Published var updateStatus = ""
    @Published var updateAvailable = false
    @Published var updating = false
    @Published var pairingExported = UserDefaults.standard.bool(forKey: "pairingExported")
    @Published var pairingAvailable = false
    @Published var keychainHint = "Подготавливаем ключ подключения…"
    @Published var controlled = false
    @Published var preview = false
    @Published var tab = UserDefaults.standard.bool(forKey: "setupCompleted") ? 0 : 1
    @Published var step = 0
    var onInput: () -> Void = {}, onAudio: () -> Void = {}, onCopy: () -> Void = {}, onExport: () -> Void = {}, onToggle: () -> Void = {}, onUpdate: () -> Void = {}, onRefresh: () -> Void = {}, onCheck: () -> Void = {}, onRepair: () -> Void = {}, onRestart: () -> Void = {}, onRetryKey: () -> Void = {}
    var onMuteChange: () -> Void = {}
    var onKeepAwakeChange: (Bool) -> Void = { _ in }
    var stateTitle: String { controlled ? "Управление на Mac" : connected ? "Всё подключено" : running ? "Mac ждёт ваш ПК" : "Давайте подключимся" }
    var ready: Bool { inputAllowed && audioAllowed && pairingAvailable && !localIP.isEmpty }
    var canContinue: Bool { step == 0 ? !localIP.isEmpty : step == 1 ? inputAllowed && audioAllowed : step == 2 ? pairingExported || connected : true }
    func finish() { UserDefaults.standard.set(true, forKey: "setupCompleted"); tab = 0 }
}

struct Dashboard: View {
    @ObservedObject var state: DashboardState
    @Environment(\.colorScheme) private var scheme
    private var mint: Color { scheme == .dark ? Color(red: 0.35, green: 0.89, blue: 0.78) : Color(red: 0.02, green: 0.44, blue: 0.37) }
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(nsImage: NSImage(named: "AppIcon") ?? NSImage()).resizable().frame(width: 42, height: 42)
                VStack(alignment: .leading, spacing: 2) {
                    Text("EdgeLink").font(.system(size: 22, weight: .bold, design: .rounded))
                    Text("Мышь, клавиатура и звук — вместе").font(.system(size: 11)).foregroundStyle(.secondary)
                }
                Spacer()
                Text(state.preview ? "PREVIEW" : "\(Updates.current)").font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary)
            }.padding(.horizontal, 26).padding(.top, 18).padding(.bottom, 16)
            Picker("Раздел", selection: $state.tab) {
                Text("Главная").tag(0); Text("Первое подключение").tag(1); Text("Параметры").tag(2)
            }.pickerStyle(.segmented).labelsHidden().padding(.horizontal, 26).padding(.bottom, 18)
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    if state.tab == 0 { home }
                    else if state.tab == 1 { setup }
                    else { preferences }
                }.padding(.horizontal, 26).padding(.bottom, 20)
            }
            Divider()
            HStack {
                Image(systemName: "keyboard"); Text("Вернуть управление на ПК")
                Spacer(); Text("Ctrl + Alt + Esc").font(.system(size: 11, weight: .medium, design: .monospaced))
            }.font(.system(size: 11)).foregroundStyle(.secondary).padding(.horizontal, 26).padding(.vertical, 14)
        }.background(Color(nsColor: .windowBackgroundColor))
        .frame(minWidth: 650, idealWidth: 720, minHeight: 600, idealHeight: 700)
        .onChange(of: state.selectedEthernet) { _, _ in state.onRefresh() }
        .onChange(of: state.automatic) { _, _ in state.onRefresh() }
        .onChange(of: state.autoStart) { _, _ in state.onRefresh() }
        .onChange(of: state.muteMac) { _, value in UserDefaults.standard.set(value, forKey: "muteMac"); state.onMuteChange() }
        .animation(.easeInOut(duration: 0.18), value: state.step)
    }
    private var home: some View {
        VStack(alignment: .leading, spacing: 18) {
            connection
            if !state.inputAllowed || !state.audioAllowed {
                HStack(spacing: 12) {
                    Image(systemName: "lock.shield").font(.system(size: 22)).foregroundStyle(.orange)
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Осталось разрешить доступ").font(.system(size: 13, weight: .semibold))
                        Text("macOS пока не разрешила этой версии управлять Mac или получать звук.").font(.system(size: 12)).foregroundStyle(.secondary)
                    }
                    Spacer(); Button("Настроить") { state.tab = 1; state.step = 1 }.buttonStyle(.bordered)
                }.card()
            }
            HStack(alignment: .top, spacing: 14) {
                VStack(alignment: .leading, spacing: 10) {
                    Label("Одна мышь и клавиатура", systemImage: "cursorarrow.motionlines").font(.system(size: 13, weight: .semibold))
                    Text("Доведите курсор до левого края экрана ПК. На Mac он появится справа.").font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }.frame(minWidth: 0, maxWidth: .infinity, alignment: .leading).card()
                VStack(alignment: .leading, spacing: 10) {
                    Label("Звук в наушниках ПК", systemImage: "headphones").font(.system(size: 13, weight: .semibold))
                    Text(state.audioHint).font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }.frame(minWidth: 0, maxWidth: .infinity, alignment: .leading).card()
            }
            keepAwakeControl
            updates
        }
    }
    private var keepAwakeControl: some View {
        HStack(spacing: 14) {
            Image(systemName: state.keepAwakeActive ? "cup.and.saucer.fill" : "cup.and.saucer")
                .font(.system(size: 23)).foregroundStyle(state.keepAwakeActive ? mint : .secondary)
            VStack(alignment: .leading, spacing: 4) {
                Text("Keep Awake").font(.system(size: 13, weight: .semibold))
                Text(state.keepAwakeHint).font(.system(size: 11)).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 12)
            Toggle("Keep Awake", isOn: Binding(get: { state.keepAwakeEnabled }, set: state.onKeepAwakeChange))
                .toggleStyle(.switch).labelsHidden().accessibilityLabel("Keep Awake — не давать Mac уснуть")
        }.card()
    }
    private var connection: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack {
                Circle().fill(state.connected ? mint : state.running ? .orange : .secondary).frame(width: 8, height: 8)
                Text(state.stateTitle).font(.system(size: 20, weight: .semibold, design: .rounded))
                Spacer(); if state.busy { ProgressView().controlSize(.small) }
            }
            HStack(spacing: 18) {
                screen("Mac", "Экран слева", state.controlled)
                VStack(spacing: 8) {
                    Image(systemName: "arrow.left.arrow.right").font(.system(size: 18)).foregroundStyle(mint)
                    Text("Ethernet").font(.system(size: 10)).foregroundStyle(.secondary)
                }
                screen("Windows", "Мышь · клавиатура · наушники", state.connected && !state.controlled)
            }
            HStack(spacing: 16) {
                Text(state.status).font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                Spacer()
                Button(state.running ? "Остановить" : "Подключить", action: state.onToggle)
                    .buttonStyle(AccentButtonStyle(color: state.running ? .secondary : mint, dark: scheme == .dark && !state.running))
                    .disabled(state.busy || state.updating || (!state.running && !state.ready))
            }
        }.padding(20).background(LinearGradient(colors: [mint.opacity(0.12), Color.blue.opacity(0.06)], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 20))
            .overlay(RoundedRectangle(cornerRadius: 20).strokeBorder(mint.opacity(0.2)))
    }
    private func screen(_ title: String, _ subtitle: String, _ active: Bool) -> some View {
        VStack(spacing: 8) {
            Image(systemName: "display").font(.system(size: 35, weight: .light)).foregroundStyle(active ? mint : .primary.opacity(0.75))
            Text(title).font(.system(size: 14, weight: .semibold))
            Text(subtitle).font(.system(size: 10)).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8)
        }.frame(maxWidth: .infinity).padding(.vertical, 14).background(Color.primary.opacity(active ? 0.05 : 0.025), in: RoundedRectangle(cornerRadius: 14))
    }
    private var setup: some View {
        VStack(alignment: .leading, spacing: 20) {
            HStack(spacing: 8) {
                ForEach(Array(["Кабель", "Разрешения", "Сопряжение", "Готово"].enumerated()), id: \.offset) { index, label in
                    HStack(spacing: 6) {
                        Image(systemName: index < state.step ? "checkmark.circle.fill" : "\(index + 1).circle.fill")
                        Text(label).font(.system(size: 11, weight: .medium))
                    }.foregroundStyle(index <= state.step ? mint : .secondary).frame(maxWidth: .infinity)
                }
            }.padding(.vertical, 8)
            VStack(alignment: .leading, spacing: 18) {
                if state.step == 0 {
                    intro("cable.connector", "Соедините компьютеры кабелем", "Используйте свободные Ethernet-порты на Mac и ПК. Интернет может остаться через Wi-Fi.")
                    Label(state.localIP.isEmpty ? "Ждём Ethernet…" : "Ethernet найден: \(state.localIP)", systemImage: state.localIP.isEmpty ? "clock" : "checkmark.circle.fill").foregroundStyle(state.localIP.isEmpty ? .secondary : mint)
                    Text("Оставьте IPv4 в автоматическом режиме. После подключения адрес может появиться не сразу.").font(.system(size: 12)).foregroundStyle(.secondary)
                    Button("Выбрать адаптер вручную") { state.tab = 2 }.buttonStyle(.bordered)
                } else if state.step == 1 {
                    intro("lock.shield", "Разрешите доступ на Mac", "Эти два разрешения нужны для мыши, клавиатуры и передачи системного звука.")
                    permissions
                } else if state.step == 2 {
                    intro("link", "Добавьте этот Mac на Windows", "Сохраните файл подключения, передайте его на ПК и откройте в EdgeLink. В Windows нажмите «Добавить Mac».")
                    HStack {
                        Button(state.pairingExported ? "Сохранить файл ещё раз…" : "Сохранить файл для ПК…", action: state.onExport).buttonStyle(AccentButtonStyle(color: mint, dark: scheme == .dark)).disabled(!state.pairingAvailable)
                        Button("Скопировать ключ", action: state.onCopy).buttonStyle(.bordered).disabled(!state.pairingAvailable)
                    }
                    if !state.pairingAvailable {
                        Text(state.keychainHint).font(.system(size: 12)).foregroundStyle(.secondary)
                        Button("Проверить ключ снова", action: state.onRetryKey).buttonStyle(.bordered)
                    }
                    if state.pairingExported { Label("Файл сохранён", systemImage: "checkmark.circle.fill").foregroundStyle(mint) }
                    Text("Файл содержит ключ доступа. Передайте его только на свой ПК; после импорта его можно удалить.").font(.system(size: 12)).foregroundStyle(.secondary)
                } else {
                    intro("checkmark.circle", state.connected ? "Компьютеры подключены" : "Mac готов к подключению", "На Windows выберите наушники и нажмите «Подключить». Затем переведите курсор за левый край экрана ПК.")
                    connection
                }
            }.card()
            HStack {
                Button("Назад") { state.step = max(0, state.step - 1) }.buttonStyle(.bordered).disabled(state.step == 0)
                Spacer()
                Text("Шаг \(state.step + 1) из 4").font(.system(size: 11)).foregroundStyle(.secondary)
                Button(state.step == 3 ? "На главный экран" : "Продолжить") {
                    if state.step == 3 { state.finish() } else { state.step += 1 }
                }.buttonStyle(AccentButtonStyle(color: mint, dark: scheme == .dark)).disabled(!state.canContinue)
            }
        }
    }
    private var permissions: some View {
        VStack(alignment: .leading, spacing: 14) {
            permission("Мышь и клавиатура", "cursorarrow.motionlines", state.inputAllowed, state.onInput)
            Divider()
            permission("Экран и системный звук", "speaker.wave.2", state.audioAllowed, state.onAudio)
            if !state.inputAllowed || !state.audioAllowed {
                Text(state.permissionHint).font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button("Проверить снова", action: state.onCheck).buttonStyle(.bordered).disabled(state.checkingPermissions)
                    Button("Уже включено, но не работает…", action: state.onRepair).buttonStyle(.bordered)
                    if state.checkingPermissions { ProgressView().controlSize(.small) }
                }
                Button("Перезапустить EdgeLink", action: state.onRestart).font(.system(size: 11)).buttonStyle(.link)
            }
        }
    }
    private var preferences: some View {
        VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 14) {
                Text("Соединение").font(.system(size: 16, weight: .semibold))
                Toggle("Выбирать Ethernet автоматически", isOn: $state.automatic).toggleStyle(.switch).disabled(state.running || state.busy)
                if state.automatic && !state.ethernet.isEmpty {
                    Picker("Ethernet", selection: $state.selectedEthernet) { ForEach(state.ethernet) { Text("\($0.name) · \($0.ip)").tag($0.id) } }.disabled(state.running || state.busy)
                }
                if !state.automatic {
                    HStack { TextField("IPv4 Mac", text: $state.localIP); TextField("IPv4 ПК (необязательно)", text: $state.pcIP) }.textFieldStyle(.roundedBorder).disabled(state.running || state.busy)
                }
                Picker("Экран Mac", selection: $state.selectedDisplay) { ForEach(state.displays, id: \.0) { Text($0.1).tag($0.0) } }.disabled(state.running || state.busy)
                Toggle("Начинать связь, когда всё готово", isOn: $state.autoStart).toggleStyle(.switch)
            }.card()
            VStack(alignment: .leading, spacing: 10) {
                Text("Звук").font(.system(size: 16, weight: .semibold))
                Toggle("Выключать динамики Mac, пока подключён ПК", isOn: $state.muteMac).toggleStyle(.switch)
                Text(state.audioHint).font(.system(size: 12)).foregroundStyle(.secondary)
                Text("Громкость сохраняется. После отключения ПК звук Mac возвращается.").font(.system(size: 11)).foregroundStyle(.secondary)
            }.card()
            VStack(alignment: .leading, spacing: 14) { Text("Доступ к Mac").font(.system(size: 16, weight: .semibold)); permissions }.card()
            updates
        }
    }
    private var updates: some View {
        HStack(spacing: 14) {
            Image(systemName: "arrow.down.circle").font(.system(size: 23)).foregroundStyle(mint)
            VStack(alignment: .leading, spacing: 4) {
                Text("Обновления из GitHub").font(.system(size: 13, weight: .medium))
                Text(state.updateStatus.isEmpty ? "Проверяем новую версию раз в сутки" : state.updateStatus).font(.system(size: 11)).foregroundStyle(.secondary)
            }
            Spacer(); if state.updating { ProgressView().controlSize(.small) }
            Button(state.updateAvailable ? "Обновить" : "Проверить", action: state.onUpdate).buttonStyle(.bordered).disabled(state.updating)
        }.card()
    }
    private func intro(_ symbol: String, _ title: String, _ subtitle: String) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Image(systemName: symbol).font(.system(size: 34, weight: .light)).foregroundStyle(mint).padding(.bottom, 4)
            Text(title).font(.system(size: 23, weight: .semibold, design: .rounded))
            Text(subtitle).font(.system(size: 13)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
    private func permission(_ name: String, _ symbol: String, _ allowed: Bool, _ action: @escaping () -> Void) -> some View {
        HStack(spacing: 12) {
            Image(systemName: symbol).font(.system(size: 19)).foregroundStyle(allowed ? mint : .secondary).frame(width: 24)
            VStack(alignment: .leading, spacing: 3) { Text(name).font(.system(size: 13, weight: .medium)); Text(allowed ? "Доступ подтверждён macOS" : "Нужен доступ для этой версии").font(.system(size: 11)).foregroundStyle(allowed ? mint : .secondary) }
            Spacer()
            if allowed { Image(systemName: "checkmark.circle.fill").font(.system(size: 23)).foregroundStyle(mint) }
            else { Button("Открыть настройки", action: action).buttonStyle(.bordered) }
        }.animation(.easeInOut(duration: 0.18), value: allowed)
    }
}
private extension View {
    func card() -> some View { frame(maxWidth: .infinity, alignment: .leading).padding(20).background(Color(nsColor: .controlBackgroundColor).opacity(0.45), in: RoundedRectangle(cornerRadius: 18)).overlay(RoundedRectangle(cornerRadius: 18).strokeBorder(.primary.opacity(0.07))) }
}
private struct AccentButtonStyle: ButtonStyle {
    let color: Color; let dark: Bool
    @Environment(\.isEnabled) private var enabled
    func makeBody(configuration: Configuration) -> some View {
        configuration.label.font(.system(size: 12, weight: .semibold)).padding(.horizontal, 14).padding(.vertical, 9)
            .foregroundStyle(enabled ? (dark ? Color.black : .white) : .secondary)
            .background(enabled ? color.opacity(configuration.isPressed ? 0.75 : 1) : Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 9))
            .scaleEffect(configuration.isPressed ? 0.98 : 1).animation(.easeOut(duration: 0.12), value: configuration.isPressed)
    }
}
