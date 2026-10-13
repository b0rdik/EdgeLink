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
    @Published var previewLight = false
    @Published var tab = UserDefaults.standard.bool(forKey: "setupCompleted") ? 0 : 1
    @Published var step = 0
    var onInput: () -> Void = {}, onAudio: () -> Void = {}, onCopy: () -> Void = {}, onExport: () -> Void = {}, onToggle: () -> Void = {}, onUpdate: () -> Void = {}, onRefresh: () -> Void = {}, onCheck: () -> Void = {}, onRepair: () -> Void = {}, onRestart: () -> Void = {}, onRetryKey: () -> Void = {}
    var onQuit: () -> Void = {}
    var onMuteChange: () -> Void = {}
    var onKeepAwakeChange: (Bool) -> Void = { _ in }
    var stateTitle: String { controlled ? "Управление на Mac" : connected ? "Подключено" : running ? "Ожидание ПК" : "Не подключено" }
    var ready: Bool { inputAllowed && audioAllowed && pairingAvailable && !localIP.isEmpty }
    var canContinue: Bool { step == 0 ? !localIP.isEmpty : step == 1 ? inputAllowed && audioAllowed : step == 2 ? pairingExported || connected : true }
    func finish() { if !preview { UserDefaults.standard.set(true, forKey: "setupCompleted") }; tab = 0 }
}

private enum DashboardPalette {
    static func background(_ scheme: ColorScheme) -> Color { Color(nsColor: .windowBackgroundColor) }
    static func card(_ scheme: ColorScheme) -> Color { Color(nsColor: .controlBackgroundColor) }
    static func secondary(_ scheme: ColorScheme) -> Color { Color(white: scheme == .dark ? 0.70 : 0.40) }
    static func inset(_ scheme: ColorScheme) -> Color { scheme == .dark ? Color(white: 0.23) : Color(white: 0.955) }
    static func accent(_ scheme: ColorScheme) -> Color { scheme == .dark ? Color(red: 0.46, green: 0.80, blue: 0.67) : Color(red: 0.15, green: 0.42, blue: 0.33) }
}

struct Dashboard: View {
    @ObservedObject var state: DashboardState
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var columns: NavigationSplitViewVisibility = .all
    private var accent: Color { DashboardPalette.accent(scheme) }
    var body: some View {
        NavigationSplitView(columnVisibility: $columns) {
            sidebar.navigationSplitViewColumnWidth(min: 155, ideal: 175, max: 210)
        } detail: {
            VStack(spacing: 0) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 24) {
                        Group {
                            if state.tab == 0 { home }
                            else if state.tab == 1 { setup }
                            else { preferences }
                        }.id(state.tab).transition(.opacity)
                    }.frame(maxWidth: 760, alignment: .leading).frame(maxWidth: .infinity, alignment: .leading)
                        .padding(24)
                }.animation(reduceMotion ? nil : .easeInOut(duration: 0.16), value: state.tab)
                footer
            }.background(DashboardPalette.background(scheme))
                .navigationTitle("EdgeLink")
                .navigationSubtitle(state.tab == 0 ? "Соединение" : state.tab == 1 ? "Первое подключение" : "Параметры")
        }.navigationSplitViewStyle(.balanced)
        .font(.system(size: 13)).tint(accent).buttonStyle(.bordered)
        .preferredColorScheme(state.preview ? (state.previewLight ? .light : .dark) : nil)
        .frame(minWidth: 650, minHeight: 600)
        .onChange(of: state.selectedEthernet) { _, _ in state.onRefresh() }
        .onChange(of: state.automatic) { _, _ in state.onRefresh() }
        .onChange(of: state.autoStart) { _, _ in state.onRefresh() }
        .onChange(of: state.muteMac) { _, value in
            guard !state.preview else { return }
            UserDefaults.standard.set(value, forKey: "muteMac"); state.onMuteChange()
        }
    }
    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 8) {
                Image(nsImage: NSImage(named: "AppIcon") ?? NSImage()).resizable().frame(width: 24, height: 24).accessibilityHidden(true)
                Text("EdgeLink").font(.system(size: 14, weight: .semibold, design: .monospaced))
            }.padding(.horizontal, 16).padding(.top, 20).padding(.bottom, 16)
            List(selection: Binding<Int?>(get: { state.tab }, set: { if let value = $0 { state.tab = value } })) {
                Label("Соединение", systemImage: "network").tag(0)
                Label("Настройка", systemImage: "link").tag(1)
                Label("Параметры", systemImage: "slider.horizontal.3").tag(2)
            }.listStyle(.sidebar)
            VStack(alignment: .leading, spacing: 10) {
                if state.preview {
                    Text("Предпросмотр").font(.system(size: 11)).foregroundStyle(.secondary)
                    Button(state.previewLight ? "Светлая тема" : "Тёмная тема") { state.previewLight.toggle() }.controlSize(.small)
                } else { Text("v\(Updates.current)").font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary) }
            }.padding(16)
        }
    }
    private var footer: some View {
        VStack(spacing: 0) {
            Divider()
            HStack {
                Text("Ctrl + Alt + Esc · вернуть на ПК").font(.system(size: 11, design: .monospaced)).foregroundStyle(DashboardPalette.secondary(scheme))
                Spacer()
                Menu("Меню") {
                    if state.preview { Toggle("Светлая тема (предпросмотр)", isOn: $state.previewLight); Divider() }
                    Button("Параметры") { state.tab = 2 }
                    Button("Проверить обновления", action: state.onUpdate).disabled(state.updating)
                    Button(state.running ? "Остановить соединение" : "Подключить", action: state.onToggle)
                        .disabled(state.busy || state.updating || (!state.running && !state.ready))
                    Divider()
                    Button("Выйти из EdgeLink", action: state.onQuit)
                }.menuStyle(.borderlessButton).tint(.primary).fixedSize().padding(.horizontal, 12).padding(.vertical, 6)
            }.padding(.horizontal, 24).frame(height: 48)
        }
    }
    private var home: some View {
        VStack(alignment: .leading, spacing: 24) {
            connection
            Divider()
            deviceStatus
            Divider()
            keepAwakeControl
            if !state.inputAllowed || !state.audioAllowed {
                HStack(alignment: .top, spacing: 12) {
                    Image(systemName: "lock").foregroundStyle(.secondary)
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Разрешения macOS").fontWeight(.medium)
                        Text("Разрешите управление Mac и захват системного звука.").foregroundStyle(.secondary)
                        Button("Настроить доступ") { state.tab = 1; state.step = 1 }
                    }
                }
            }
            Text("Закрытие окна оставляет EdgeLink в строке меню.")
                .font(.system(size: 11)).foregroundStyle(.secondary)
        }
    }
    private var inputStatus: String {
        !state.inputAllowed ? "Нет доступа" : state.connected ? (state.controlled ? "На MacBook" : "На ПК") : state.running ? "Ожидание ПК" : "Не подключено"
    }
    private var audioStatus: String {
        !state.audioAllowed ? "Нет доступа" : state.connected ? "Передаётся" : state.running ? "Готов" : "Не передаётся"
    }
    private var deviceStatus: some View {
        VStack(spacing: 0) {
            statusItem("Клавиатура", "keyboard", inputStatus, state.connected && state.inputAllowed)
                .help("Клавиатура ПК управляет MacBook при переходе курсора через край экрана.")
            statusItem("Мышь", "computermouse", inputStatus, state.connected && state.inputAllowed)
                .help("Доведите курсор до левого края экрана ПК. Для возврата используйте правый край MacBook.")
            statusItem("Звук", state.connected ? "speaker.wave.2" : "speaker", audioStatus, state.connected && state.audioAllowed)
                .help(state.audioHint)
        }
    }
    private func statusItem(_ title: String, _ symbol: String, _ status: String, _ active: Bool) -> some View {
        HStack(spacing: 12) {
            Image(systemName: symbol).font(.system(size: 16)).frame(width: 22)
                .foregroundStyle(active ? accent : DashboardPalette.secondary(scheme)).accessibilityHidden(true)
            Text(title)
            Spacer(minLength: 12)
            Text(status).font(.system(size: 12, design: .monospaced))
                .foregroundStyle(active ? accent : DashboardPalette.secondary(scheme)).contentTransition(.opacity)
        }.frame(minHeight: 38)
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.18), value: status)
    }
    private var keepAwakeControl: some View {
        HStack(spacing: 20) {
            VStack(alignment: .leading, spacing: 10) {
                Text("Keep Awake").font(.system(size: 13, weight: .medium))
                Text(state.keepAwakeHint).font(.system(size: 12)).foregroundStyle(DashboardPalette.secondary(scheme)).fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
            Toggle("Keep Awake", isOn: Binding(get: { state.keepAwakeEnabled }, set: state.onKeepAwakeChange))
                .toggleStyle(.switch).controlSize(.small).labelsHidden()
                .accessibilityLabel("Keep Awake — не давать Mac уснуть")
        }
    }
    private var connection: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack(spacing: 16) {
                screen("MacBook", "laptopcomputer")
                Image(systemName: "arrow.left.arrow.right").font(.system(size: 13)).foregroundStyle(.secondary)
                screen("ПК", "display")
            }.padding(.vertical, 18).padding(.horizontal, 16)
                .background(DashboardPalette.card(scheme), in: RoundedRectangle(cornerRadius: 6))
                .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color.primary.opacity(0.09)))
            HStack(spacing: 10) {
                Circle().fill(state.connected ? accent : .secondary).frame(width: 6, height: 6).accessibilityHidden(true)
                Text(state.stateTitle).font(.system(size: 14, weight: .medium, design: .monospaced)).contentTransition(.opacity)
                Spacer(minLength: 8)
                if state.busy { ProgressView().controlSize(.small) }
                Group {
                    if state.running { Button("Остановить", action: state.onToggle) }
                    else { Button("Подключить", action: state.onToggle).buttonStyle(.borderedProminent) }
                }.disabled(state.busy || state.updating || (!state.running && !state.ready))
            }
            Text(state.status).font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }.animation(reduceMotion ? nil : .easeInOut(duration: 0.18), value: state.connected)
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.18), value: state.controlled)
    }
    private func screen(_ title: String, _ symbol: String) -> some View {
        HStack(spacing: 10) {
            Image(systemName: symbol).font(.system(size: 22)).accessibilityHidden(true)
            Text(title).font(.system(size: 13, weight: .medium, design: .monospaced))
        }.frame(maxWidth: .infinity)
    }
    private var setup: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 8) {
                ForEach(Array(["Кабель", "Разрешения", "Сопряжение", "Готово"].enumerated()), id: \.offset) { index, label in
                    VStack(spacing: 6) {
                        ZStack {
                            Circle().fill(index <= state.step ? accent : DashboardPalette.inset(scheme)).frame(width: 24, height: 24)
                            if index < state.step { Image(systemName: "checkmark").font(.system(size: 11, weight: .semibold)) }
                            else { Text("\(index + 1)").font(.system(size: 12, weight: .semibold)) }
                        }.foregroundStyle(index <= state.step ? (scheme == .dark ? Color.black : .white) : .secondary)
                        Text(label).font(.system(size: 12)).foregroundStyle(index <= state.step ? accent : .secondary)
                    }.frame(maxWidth: .infinity)
                }
            }.padding(.vertical, 6)
            VStack(alignment: .leading, spacing: 16) {
                if state.step == 0 {
                    intro("1. Соедините компьютеры", "Подключите свободные Ethernet-порты Mac и ПК кабелем. Интернет может остаться через Wi-Fi.")
                    Label(state.localIP.isEmpty ? "Ожидание Ethernet" : "Ethernet найден: \(state.localIP)", systemImage: state.localIP.isEmpty ? "clock" : "checkmark.circle.fill")
                        .foregroundStyle(state.localIP.isEmpty ? .secondary : accent)
                    Text("Оставьте IPv4 в автоматическом режиме. Адрес может появиться не сразу.").foregroundStyle(DashboardPalette.secondary(scheme))
                    Button("Выбрать адаптер вручную") { state.tab = 2 }
                } else if state.step == 1 {
                    intro("2. Разрешите доступ на Mac", "Два разрешения нужны для мыши, клавиатуры и передачи системного звука.")
                    permissions
                } else if state.step == 2 {
                    intro("3. Добавьте Mac на Windows", "Сохраните файл подключения, передайте его на ПК и откройте в EdgeLink кнопкой «Добавить Mac».")
                    HStack(spacing: 12) {
                        Button(state.pairingExported ? "Сохранить файл ещё раз…" : "Сохранить файл для ПК…", action: state.onExport)
                            .buttonStyle(.borderedProminent).disabled(!state.pairingAvailable)
                        Button("Скопировать ключ", action: state.onCopy).disabled(!state.pairingAvailable)
                    }
                    if !state.pairingAvailable {
                        Text(state.keychainHint).foregroundStyle(DashboardPalette.secondary(scheme))
                        Button("Проверить ключ снова", action: state.onRetryKey)
                    }
                    if state.pairingExported { Label("Файл сохранён", systemImage: "checkmark.circle.fill").foregroundStyle(accent) }
                    Text("Файл содержит ключ доступа. Передайте его только на свой ПК; после импорта его можно удалить.").foregroundStyle(DashboardPalette.secondary(scheme))
                } else {
                    intro("4. Проверьте подключение", "На Windows выберите наушники и нажмите «Подключить». Затем переведите курсор за левый край экрана ПК.")
                    connection
                }
            }.dashboardCard()
            HStack(spacing: 12) {
                Button("Назад") { state.step = max(0, state.step - 1) }.disabled(state.step == 0)
                Button(state.step == 3 ? "На главный экран" : "Продолжить") {
                    if state.step == 3 { state.finish() } else { state.step += 1 }
                }.buttonStyle(.borderedProminent).disabled(!state.canContinue)
                Spacer()
                Text("Шаг \(state.step + 1) из 4").font(.system(size: 12)).foregroundStyle(DashboardPalette.secondary(scheme))
            }
        }
    }
    private var permissions: some View {
        VStack(alignment: .leading, spacing: 14) {
            permission("Мышь и клавиатура", "cursorarrow", state.inputAllowed, state.onInput)
            Divider()
            permission("Экран и системный звук", "speaker.wave.2", state.audioAllowed, state.onAudio)
            if !state.inputAllowed || !state.audioAllowed {
                Text(state.permissionHint).foregroundStyle(DashboardPalette.secondary(scheme)).fixedSize(horizontal: false, vertical: true)
                HStack(spacing: 12) {
                    Button("Проверить снова", action: state.onCheck).disabled(state.checkingPermissions)
                    Button("Разрешение не определяется…", action: state.onRepair)
                    if state.checkingPermissions { ProgressView().controlSize(.small) }
                }
                Button("Перезапустить EdgeLink", action: state.onRestart)
            }
        }
    }
    private var preferences: some View {
        VStack(alignment: .leading, spacing: 16) {
            VStack(alignment: .leading, spacing: 16) {
                intro("Соединение", "Обычно достаточно автоматического выбора Ethernet.")
                Toggle("Выбирать Ethernet автоматически", isOn: $state.automatic).toggleStyle(.switch).controlSize(.small).disabled(state.running || state.busy)
                if state.automatic && !state.ethernet.isEmpty {
                    field("Ethernet") {
                        Picker("Ethernet", selection: $state.selectedEthernet) { ForEach(state.ethernet) { Text("\($0.name) · \($0.ip)").tag($0.id) } }.labelsHidden()
                    }.disabled(state.running || state.busy)
                }
                if !state.automatic {
                    field("IPv4 Mac") { TextField("IPv4 Mac", text: $state.localIP).textFieldStyle(.roundedBorder) }
                        .disabled(state.running || state.busy)
                    field("IPv4 ПК") { TextField("Необязательно", text: $state.pcIP).textFieldStyle(.roundedBorder) }
                        .disabled(state.running || state.busy)
                }
                field("Экран Mac") {
                    Picker("Экран Mac", selection: $state.selectedDisplay) { ForEach(state.displays, id: \.0) { Text($0.1).tag($0.0) } }.labelsHidden()
                }.disabled(state.running || state.busy)
                Toggle("Начинать связь, когда всё готово", isOn: $state.autoStart).toggleStyle(.switch).controlSize(.small)
            }.dashboardCard()
            VStack(alignment: .leading, spacing: 14) {
                intro("Звук", "Полученный звук Mac смешивается со звуком Windows.")
                Toggle("Выключать динамики Mac, пока подключён ПК", isOn: $state.muteMac).toggleStyle(.switch).controlSize(.small)
                Text(state.audioHint).foregroundStyle(DashboardPalette.secondary(scheme))
                Text("После отключения ПК звук Mac возвращается.").foregroundStyle(DashboardPalette.secondary(scheme))
            }.dashboardCard()
            keepAwakeControl
            VStack(alignment: .leading, spacing: 16) { intro("Доступ к Mac", "Разрешения проверяются автоматически после возврата из системных настроек."); permissions }.dashboardCard()
            updates
        }
    }
    private var updates: some View {
        VStack(alignment: .leading, spacing: 12) {
            intro("Обновления", "Новые версии из b0rdik/EdgeLink. Установка — после подтверждения.")
            HStack(spacing: 12) {
                Button(state.updateAvailable ? "Установить обновление" : "Проверить обновления", action: state.onUpdate).disabled(state.updating)
                if state.updating { ProgressView().controlSize(.small) }
            }
            if !state.updateStatus.isEmpty { Text(state.updateStatus).foregroundStyle(DashboardPalette.secondary(scheme)).fixedSize(horizontal: false, vertical: true) }
        }.dashboardCard()
    }
    private func intro(_ title: String, _ subtitle: String) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.system(size: 15, weight: .medium))
            Text(subtitle).foregroundStyle(DashboardPalette.secondary(scheme)).fixedSize(horizontal: false, vertical: true)
        }
    }
    private func field<Editor: View>(_ caption: String, @ViewBuilder editor: () -> Editor) -> some View {
        HStack(spacing: 20) {
            Text(caption).foregroundStyle(DashboardPalette.secondary(scheme)).frame(width: 128, alignment: .leading)
            editor().frame(maxWidth: .infinity)
        }
    }
    private func permission(_ name: String, _ symbol: String, _ allowed: Bool, _ action: @escaping () -> Void) -> some View {
        HStack(spacing: 12) {
            Image(systemName: symbol).font(.system(size: 17)).foregroundStyle(DashboardPalette.secondary(scheme)).frame(width: 22).accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 4) {
                Text(name).font(.system(size: 14, weight: .semibold))
                Text(allowed ? "Разрешено" : "Требуется разрешение").font(.system(size: 12)).foregroundStyle(allowed ? accent : .secondary)
            }
            Spacer(minLength: 12)
            if allowed { Label("Разрешено", systemImage: "checkmark.circle.fill").labelStyle(.iconOnly).foregroundStyle(accent).font(.system(size: 18)) }
            else { Button("Открыть настройки", action: action).fixedSize() }
        }.animation(reduceMotion ? nil : .easeOut(duration: 0.14), value: allowed)
    }
}
private struct DashboardCard: ViewModifier {
    @Environment(\.colorScheme) private var scheme
    var highlighted: Bool
    func body(content: Content) -> some View {
        content.frame(maxWidth: .infinity, alignment: .leading).padding(18)
            .background(DashboardPalette.card(scheme), in: RoundedRectangle(cornerRadius: 6))
            .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color.primary.opacity(0.08)))
    }
}
private extension View {
    func dashboardCard(highlighted: Bool = false) -> some View { modifier(DashboardCard(highlighted: highlighted)) }
}
