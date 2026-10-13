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
    static func background(_ scheme: ColorScheme) -> Color { scheme == .dark ? Color(white: 0.125) : Color(white: 0.95) }
    static func card(_ scheme: ColorScheme) -> Color { scheme == .dark ? Color(white: 0.195) : Color(white: 0.985) }
    static func secondary(_ scheme: ColorScheme) -> Color { Color(white: scheme == .dark ? 0.70 : 0.40) }
    static func inset(_ scheme: ColorScheme) -> Color { scheme == .dark ? Color(white: 0.23) : Color(white: 0.955) }
    static func accent(_ scheme: ColorScheme) -> Color { scheme == .dark ? Color(red: 89/255, green: 226/255, blue: 199/255) : Color(red: 0.02, green: 0.44, blue: 0.37) }
}

struct Dashboard: View {
    @ObservedObject var state: DashboardState
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Namespace private var navigationAnimation
    private var accent: Color { DashboardPalette.accent(scheme) }
    var body: some View {
        VStack(spacing: 0) {
            header
            navigation
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if state.tab == 0 { home }
                    else if state.tab == 1 { setup }
                    else { preferences }
                }.padding(.horizontal, 24).padding(.top, 16).padding(.bottom, 20)
            }.id(state.tab).transition(.opacity)
                .animation(reduceMotion ? nil : .easeInOut(duration: 0.16), value: state.tab)
            footer
        }
        .font(.system(size: 13)).tint(accent)
        .buttonStyle(DashboardButtonStyle())
        .background(DashboardPalette.background(scheme))
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
    private var header: some View {
        HStack(spacing: 16) {
            Image(nsImage: NSImage(named: "AppIcon") ?? NSImage()).resizable().frame(width: 36, height: 36).accessibilityHidden(true)
            Text("EdgeLink").font(.system(size: 22, weight: .semibold))
            Spacer()
            Text(state.preview ? "Предпросмотр" : "v\(Updates.current)").font(.system(size: 12)).foregroundStyle(DashboardPalette.secondary(scheme))
        }.padding(.horizontal, 24).frame(height: 64)
    }
    private var navigation: some View {
        HStack(spacing: 6) {
            tab("Главная", "house", 0)
            tab("Первое подключение", "link", 1)
            tab("Параметры", "gearshape", 2)
            Spacer(minLength: 0)
        }.padding(.horizontal, 24).frame(height: 48)
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.22), value: state.tab)
    }
    private func tab(_ title: String, _ symbol: String, _ index: Int) -> some View {
        Button { withAnimation(reduceMotion ? nil : .easeInOut(duration: 0.22)) { state.tab = index } } label: {
            VStack(spacing: 0) {
                Label(title, systemImage: symbol)
                    .font(.system(size: 13, weight: state.tab == index ? .semibold : .regular))
                    .frame(height: 38).padding(.horizontal, 12)
                ZStack {
                    Color.clear
                    if state.tab == index { Capsule().fill(accent).matchedGeometryEffect(id: "tab", in: navigationAnimation) }
                }.frame(width: 28, height: 3)
            }.frame(height: 44).contentShape(Rectangle())
        }.buttonStyle(.plain)
        .accessibilityAddTraits(state.tab == index ? .isSelected : [])
        .help(title)
    }
    private var footer: some View {
        VStack(spacing: 0) {
            Divider()
            HStack {
                Text("Вернуть управление на ПК · Ctrl + Alt + Esc").font(.system(size: 12)).foregroundStyle(DashboardPalette.secondary(scheme))
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
        VStack(alignment: .leading, spacing: 16) {
            connection
            if !state.inputAllowed || !state.audioAllowed {
                HStack(spacing: 16) {
                    VStack(alignment: .leading, spacing: 5) {
                        Text("Разрешения macOS").font(.system(size: 16, weight: .semibold))
                        Text("Разрешите управление Mac и захват системного звука.").foregroundStyle(DashboardPalette.secondary(scheme))
                    }
                    Spacer()
                    Button("Настроить") { state.tab = 1; state.step = 1 }
                }.dashboardCard()
            }
            deviceStatus
            keepAwakeControl
            Text("Закрытие окна оставляет EdgeLink в строке меню. Соединение продолжает работать.")
                .font(.system(size: 12)).foregroundStyle(DashboardPalette.secondary(scheme)).padding(.horizontal, 4)
        }
    }
    private var inputStatus: String {
        !state.inputAllowed ? "Нет доступа" : state.connected ? (state.controlled ? "На MacBook" : "На ПК") : state.running ? "Ожидание ПК" : "Не подключено"
    }
    private var audioStatus: String {
        !state.audioAllowed ? "Нет доступа" : state.connected ? "Передаётся" : state.running ? "Готов" : "Не передаётся"
    }
    private var deviceStatus: some View {
        HStack(spacing: 18) {
            statusItem("Клавиатура", "keyboard", inputStatus, state.connected && state.inputAllowed)
                .help("Клавиатура ПК управляет MacBook при переходе курсора через край экрана.")
            Divider().frame(height: 38)
            statusItem("Мышь", "computermouse", inputStatus, state.connected && state.inputAllowed)
                .help("Доведите курсор до левого края экрана ПК. Для возврата используйте правый край MacBook.")
            Divider().frame(height: 38)
            statusItem("Звук", state.connected ? "speaker.wave.2" : "speaker", audioStatus, state.connected && state.audioAllowed)
                .help(state.audioHint)
        }.dashboardCard()
    }
    private func statusItem(_ title: String, _ symbol: String, _ status: String, _ active: Bool) -> some View {
        HStack(spacing: 10) {
            Image(systemName: symbol).font(.system(size: 22)).frame(width: 34, height: 38)
                .foregroundStyle(active ? accent : DashboardPalette.secondary(scheme))
                .symbolEffect(.bounce, value: reduceMotion ? false : active)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.system(size: 13, weight: .medium))
                Text(status).font(.system(size: 11)).foregroundStyle(DashboardPalette.secondary(scheme))
                    .contentTransition(.opacity)
            }
            Spacer(minLength: 0)
        }.frame(maxWidth: .infinity, alignment: .leading)
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.18), value: status)
    }
    private var keepAwakeControl: some View {
        HStack(spacing: 20) {
            VStack(alignment: .leading, spacing: 10) {
                Text("Keep Awake").font(.system(size: 17, weight: .semibold))
                Text(state.keepAwakeHint).foregroundStyle(DashboardPalette.secondary(scheme)).fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
            Toggle("Keep Awake", isOn: Binding(get: { state.keepAwakeEnabled }, set: state.onKeepAwakeChange))
                .toggleStyle(.switch).controlSize(.small).labelsHidden()
                .accessibilityLabel("Keep Awake — не давать Mac уснуть")
        }.dashboardCard()
    }
    private var connection: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 10) {
                Circle().fill(state.connected ? accent : state.running ? .orange : .secondary).frame(width: 7, height: 7).accessibilityHidden(true)
                Text(state.stateTitle).font(.system(size: 23, weight: .semibold))
                Spacer()
                if state.busy { ProgressView().controlSize(.small) }
            }
            HStack(spacing: 16) {
                screen("MacBook", "Экран слева", "laptopcomputer", state.controlled)
                VStack(spacing: 8) {
                    Image(systemName: "arrow.left.arrow.right").font(.system(size: 22))
                    Text("Ethernet").font(.system(size: 11)).foregroundStyle(DashboardPalette.secondary(scheme))
                }.frame(width: 64)
                screen("ПК", "Windows", "display", state.connected && !state.controlled)
            }
            HStack(spacing: 20) {
                Text(state.status).fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
                Button(state.running ? "Остановить" : "Подключить", action: state.onToggle)
                    .buttonStyle(DashboardButtonStyle(primary: !state.running)).fixedSize()
                    .disabled(state.busy || state.updating || (!state.running && !state.ready))
            }
        }.dashboardCard()
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.2), value: state.connected)
            .animation(reduceMotion ? nil : .easeInOut(duration: 0.2), value: state.controlled)
    }
    private func screen(_ title: String, _ subtitle: String, _ symbol: String, _ active: Bool) -> some View {
        VStack(spacing: 10) {
            Image(systemName: symbol).font(.system(size: 36, weight: .regular)).frame(height: 44).accessibilityHidden(true)
            Text(title).font(.system(size: 16, weight: .semibold))
            Text(subtitle).font(.system(size: 11)).foregroundStyle(DashboardPalette.secondary(scheme)).multilineTextAlignment(.center)
        }.frame(maxWidth: .infinity).frame(minHeight: 132)
            .padding(.horizontal, 8)
            .background(DashboardPalette.inset(scheme), in: RoundedRectangle(cornerRadius: 10))
            .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(active ? accent : Color.primary.opacity(0.055)))
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
                            .buttonStyle(DashboardButtonStyle(primary: true)).disabled(!state.pairingAvailable)
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
                }.buttonStyle(DashboardButtonStyle(primary: true)).disabled(!state.canContinue)
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
            Text(title).font(.system(size: 19, weight: .semibold))
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
        content.frame(maxWidth: .infinity, alignment: .leading).padding(22)
            .background(DashboardPalette.card(scheme), in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(highlighted ? DashboardPalette.accent(scheme).opacity(0.3) : Color.primary.opacity(0.055)))
    }
}
private extension View {
    func dashboardCard(highlighted: Bool = false) -> some View { modifier(DashboardCard(highlighted: highlighted)) }
}
private struct DashboardButtonStyle: ButtonStyle {
    var primary = false
    func makeBody(configuration: Configuration) -> some View { DashboardButtonFace(configuration: configuration, primary: primary) }
}
private struct DashboardButtonFace: View {
    let configuration: ButtonStyleConfiguration
    let primary: Bool
    @Environment(\.colorScheme) private var scheme
    @Environment(\.isEnabled) private var enabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var hovered = false
    var body: some View {
        configuration.label.font(.system(size: 13))
            .padding(.horizontal, 18).padding(.vertical, 9).frame(minHeight: 36)
            .foregroundStyle(enabled ? (primary && scheme == .dark ? Color.black : primary ? .white : .primary) : .secondary)
            .background(enabled && primary ? DashboardPalette.accent(scheme).opacity(configuration.isPressed ? 0.8 : 1) : Color.primary.opacity(configuration.isPressed ? 0.1 : hovered ? 0.075 : 0.045), in: RoundedRectangle(cornerRadius: 5))
            .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(Color.primary.opacity(primary ? 0 : 0.09)))
            .opacity(enabled ? 1 : 0.65)
            .scaleEffect(!reduceMotion && configuration.isPressed ? 0.98 : 1)
            .onHover { hovered = enabled && $0 }
            .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: configuration.isPressed)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: hovered)
    }
}
