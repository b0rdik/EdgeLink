import AppKit
import SwiftUI
import ApplicationServices
import Security
import CryptoKit

enum PairingSecret {
    static func load() throws -> Data {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "local.edgelink.pairing", kSecAttrAccount as String: "default",
            kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var found: CFTypeRef?
        let result = SecItemCopyMatching(query as CFDictionary, &found)
        if result == errSecSuccess, let data = found as? Data, data.count == 32 { return data }
        guard result == errSecItemNotFound else { throw LinkError.message("Не удалось прочитать ключ из Keychain (\(result))") }
        var bytes = [UInt8](repeating: 0, count: 32)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else { throw LinkError.message("Не удалось создать ключ") }
        let data = Data(bytes)
        let add: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "local.edgelink.pairing",
            kSecAttrAccount as String: "default", kSecValueData as String: data, kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock]
        let status = SecItemAdd(add as CFDictionary, nil)
        guard status == errSecSuccess else { throw LinkError.message("Не удалось сохранить ключ в Keychain (\(status))") }
        return data
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let state = DashboardState()
    private let discovery = DiscoveryServer()
    private var monitor: Timer?
    private var scanningNetwork = false
    private var initialSetupApplied = false
    private var loadingSecret = false
    private var automaticPaused = false
    private var retryAfter = Date.distantPast
    private var boundIP = ""
    private var audioProbeValidUntil = Date.distantPast
    private let preview = CommandLine.arguments.contains("--preview") || CommandLine.arguments.contains("--preview-export")
    private var item: NSStatusItem!
    private var window: NSWindow!
    private var availableUpdate: ReleaseUpdate?
    private var updating = false { didSet { state.updating = updating } }
    private var updateTask: Task<Void, Never>?
    private let input = InputReceiver()
    private lazy var server = LinkServer(input: input)
    private lazy var audio = AudioSender(server: server)
    private let outputMute = OutputMute()
    private var running = false { didSet { state.running = running } }
    private var busy = false { didSet { state.busy = busy } }
    private var secret: Data?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.image = menuIcon(); item.button?.toolTip = "EdgeLink"
        let menu = NSMenu()
        menu.addItem(withTitle: "Настройки…", action: #selector(show), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Остановить связь", action: #selector(stopLink), keyEquivalent: "").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Завершить EdgeLink", action: #selector(quit), keyEquivalent: "q").target = self
        item.menu = menu
        let appMenu = NSMenu(), appRoot = NSMenuItem(); appRoot.submenu = appMenu
        appMenu.addItem(withTitle: "Завершить EdgeLink", action: #selector(quit), keyEquivalent: "q").target = self
        let main = NSMenu(); main.addItem(appRoot); NSApp.mainMenu = main
        configureActions(); buildWindow()
        if preview { renderPreview(); return }
        outputMute.recover()
        show()
        loadPairingSecret()
        server.audioBatchFrames = { [weak self] in self?.audio.captureBatchFrames ?? 0 }
        server.status = { [weak self] text in guard let self else { return }; state.status = text; state.connected = server.currentSession != nil; refreshOutputMute() }
        audio.failed = { [weak self] text in
            guard let self else { return }; let paused = automaticPaused; stopLink(); automaticPaused = paused
            retryAfter = Date(timeIntervalSinceNow: 10); state.status = text
        }
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(suspendLink), name: NSWorkspace.willSleepNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(suspendLink), name: NSWorkspace.sessionDidResignActiveNotification, object: nil)
        input.changed = { [weak self] active in self?.state.controlled = active }
        NotificationCenter.default.addObserver(self, selector: #selector(refreshSetup), name: NSApplication.didBecomeActiveNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(resumeLink), name: NSWorkspace.didWakeNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(resumeLink), name: NSWorkspace.sessionDidBecomeActiveNotification, object: nil)
        monitor = Timer(timeInterval: 1, repeats: true) { [weak self] _ in self?.refreshSetup() }
        if let monitor { RunLoop.main.add(monitor, forMode: .common) }
        refreshSetup()
        if state.tab == 1 && !state.localIP.isEmpty { state.step = state.inputAllowed && state.audioAllowed ? 2 : 1 }
        checkUpdates(manual: false)
    }
    private func configureActions() {
        state.onMuteChange = { [weak self] in self?.refreshOutputMute() }
        state.onInput = { [weak self] in self?.requestInput() }
        state.onAudio = { [weak self] in self?.requestAudio() }
        state.onCopy = { [weak self] in self?.copyKey() }
        state.onExport = { [weak self] in self?.exportPairing() }
        state.onToggle = { [weak self] in guard let self else { return }; automaticPaused = false; toggle() }
        state.onUpdate = { [weak self] in self?.updateClicked() }
        state.onRefresh = { [weak self] in self?.refreshSetup() }
        state.onCheck = { [weak self] in self?.checkPermissions() }
        state.onRepair = { [weak self] in self?.repairPermissions() }
        state.onRestart = { [weak self] in self?.restart() }
        state.onRetryKey = { [weak self] in self?.loadPairingSecret() }
    }
    private func refreshOutputMute() {
        guard !preview else { return }
        outputMute.update(connected: server.currentSession != nil, enabled: state.muteMac)
        state.audioHint = outputMute.hint
    }
    private func buildWindow() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 720, height: min(720, max(630, (NSScreen.main?.visibleFrame.height ?? 850) - 70))), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "EdgeLink"; window.titlebarAppearsTransparent = true
        window.minSize = NSSize(width: 680, height: 640); window.center(); window.isReleasedWhenClosed = false
        for screen in NSScreen.screens {
            let id = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? CGMainDisplayID()
            let bounds = CGDisplayBounds(id)
            state.displays.append((id, "\(screen.localizedName) · \(Int(bounds.width))×\(Int(bounds.height))"))
        }
        state.selectedDisplay = CGMainDisplayID()
        window.contentView = NSHostingView(rootView: Dashboard(state: state))
    }
    private func menuIcon() -> NSImage {
        let image = NSImage(named: "EdgeLinkStatus") ?? NSImage(size: NSSize(width: 23, height: 18))
        image.size = NSSize(width: 23, height: 18)
        image.isTemplate = true
        return image
    }
    private func renderPreview() {
        state.preview = true; state.localIP = "169.254.42.8"
        state.ethernet = [EthernetAddress(id: "preview", name: "Ethernet", ip: state.localIP)]; state.selectedEthernet = "preview"
        state.pairingAvailable = true; state.inputAllowed = true; state.audioAllowed = false; state.tab = 1; state.step = 1
        state.status = "Разрешите захват системного звука — остальные шаги готовы"
        if CommandLine.arguments.contains("--preview-home") { state.tab = 0; state.audioAllowed = true; state.connected = true; state.running = true; state.status = "Mac и ПК соединены. Звук передаётся в наушники ПК." }
        if let index = CommandLine.arguments.firstIndex(of: "--preview-export"), CommandLine.arguments.count > index + 1 {
            NSApp.appearance = NSAppearance(named: .darkAqua); window.orderFrontRegardless()
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [self] in
                guard let view = window.contentView, let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { exit(1) }
                view.cacheDisplay(in: view.bounds, to: bitmap)
                do { try bitmap.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: CommandLine.arguments[index + 1])); exit(0) } catch { exit(1) }
            }
        } else { show() }
    }
    @objc private func refreshSetup() {
        refreshOutputMute()
        guard !preview else { return }
        state.inputAllowed = Permissions.inputAllowed; state.audioAllowed = Permissions.audioAllowed || audio.isCapturing || Date() < audioProbeValidUntil
        guard !scanningNetwork else { return }; scanningNetwork = true
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let addresses = Ethernet.addresses()
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }; scanningNetwork = false; applyAddresses(addresses)
            }
        }
    }
    private func applyAddresses(_ addresses: [EthernetAddress]) {
        if state.ethernet != addresses { state.ethernet = addresses }
        if state.automatic {
            if !addresses.contains(where: { $0.id == state.selectedEthernet }) {
                state.selectedEthernet = addresses.first(where: { $0.ip == state.localIP })?.id ?? addresses.first?.id ?? ""
            }
            state.localIP = addresses.first(where: { $0.id == state.selectedEthernet })?.ip ?? ""
        }
        if !initialSetupApplied {
            initialSetupApplied = true
            if state.tab == 1 && !state.localIP.isEmpty { state.step = state.inputAllowed && state.audioAllowed ? 2 : 1 }
        }
        UserDefaults.standard.set(state.automatic, forKey: "automatic"); UserDefaults.standard.set(state.autoStart, forKey: "autoStart")
        if running && (!state.inputAllowed || !state.audioAllowed || (state.automatic && boundIP != state.localIP)) {
            stopLink(); automaticPaused = false
        }
        if state.autoStart && !automaticPaused && !running && !busy && !updating && state.ready && Date() >= retryAfter { toggle() }
    }
    private func loadPairingSecret() {
        guard !preview && !loadingSecret else { return }; loadingSecret = true
        state.keychainHint = "Подтвердите доступ EdgeLink к сохранённому ключу, если macOS показывает запрос Keychain."
        Task { @MainActor in
            do { secret = try await Task.detached { try PairingSecret.load() }.value; state.pairingAvailable = true; refreshSetup() }
            catch { state.pairingAvailable = false; state.keychainHint = error.localizedDescription; state.status = error.localizedDescription }
            loadingSecret = false
        }
    }
    private func exportPairing() {
        guard let secret else { return }
        let panel = NSSavePanel(); panel.nameFieldStringValue = "Мой Mac.edgelink"; panel.title = "Файл подключения для Windows"
        panel.message = "Файл содержит ключ доступа к Mac. Передайте его только на свой ПК."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let data = try Wire.json(["version": 1, "repository": Updates.repository, "key": secret.base64EncodedString(), "macName": Host.current().localizedName ?? "Mac"])
            try data.write(to: url, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
            state.pairingExported = true; UserDefaults.standard.set(true, forKey: "pairingExported"); state.status = "Файл сохранён. Откройте его в EdgeLink на Windows."
        } catch { state.status = error.localizedDescription }
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        show()
        return false
    }
    @objc private func show() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    @objc private func updateClicked() {
        if let update = availableUpdate { offerUpdate(update) } else { checkUpdates(manual: true) }
    }
    private func checkUpdates(manual: Bool) {
        guard !updating else { return }
        let defaults = UserDefaults.standard
        if !manual, let last = defaults.object(forKey: "updateLastAttempt") as? Date, Date().timeIntervalSince(last) < 86400 { return }
        defaults.set(Date(), forKey: "updateLastAttempt")
        updating = true; state.updateStatus = "Проверка GitHub Releases…"
        updateTask = Task { @MainActor in
            do {
                let update = try await Updates.check()
                availableUpdate = update; state.updateAvailable = update != nil
                state.updateStatus = update.map { "Доступна версия \($0.version)" } ?? "Установлена актуальная версия \(Updates.current)"
                updating = false
                if manual, let update { offerUpdate(update) }
            } catch {
                updating = false
                state.updateStatus = manual ? error.localizedDescription : "Не удалось проверить обновления. Доступна ручная проверка."
            }
        }
    }
    private func offerUpdate(_ update: ReleaseUpdate) {
        guard !updating && !busy else { state.updateStatus = "Дождитесь завершения текущей операции"; return }
        let alert = NSAlert(); alert.messageText = "Установить EdgeLink \(update.version)?"
        alert.informativeText = "Приложение скачает обновление из b0rdik/EdgeLink, проверит SHA-256 и перезапустится. Предыдущая версия сохранится рядом с приложением."
        alert.addButton(withTitle: "Установить"); alert.addButton(withTitle: "Позже")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        updating = true; state.updateStatus = "Скачивание и проверка обновления…"
        updateTask = Task { @MainActor in
            do {
                let staged = try await Updates.stage(update)
                let helper = try Updates.installer(staged)
                _ = helper // detached helper survives this application's termination
                busy = true; server.stop(); discovery.stop(); await audio.stop(); running = false
                NSApp.terminate(nil)
            } catch {
                updating = false
                state.updateStatus = "Обновление не установлено: \(error.localizedDescription)"
            }
        }
    }
    @objc private func copyKey() {
        guard let secret else { return }
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(secret.base64EncodedString(), forType: .string)
        state.status = "Ключ скопирован. Вставьте его в EdgeLink на ПК."
    }
    @objc private func requestInput() {
        guard !preview else { return }
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!); refreshSetup()
    }
    @objc private func requestAudio() { guard !preview else { return }; _ = CGRequestScreenCaptureAccess(); NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!); refreshSetup() }
    private func checkPermissions() {
        guard !preview && !state.checkingPermissions else { return }
        refreshSetup(); state.checkingPermissions = true
        Task { @MainActor in
            do { try await Permissions.verifyAudio(); audioProbeValidUntil = Date(timeIntervalSinceNow: 10); state.audioAllowed = true }
            catch { state.audioAllowed = false; state.permissionHint = "Если переключатели уже включены, macOS могла сохранить доступ для старой подписи. Нажмите «Уже включено, но не работает…»." }
            state.inputAllowed = Permissions.inputAllowed; state.checkingPermissions = false
            if state.inputAllowed && state.audioAllowed { state.permissionHint = "Оба разрешения подтверждены." }
        }
    }
    private func repairPermissions() {
        guard !preview && !busy && !updating else { return }
        let alert = NSAlert(); alert.messageText = "Обновить разрешения EdgeLink?"
        alert.informativeText = "После замены старой сборки macOS может показывать включённые переключатели, но отклонять её подпись. Будут сброшены только разрешения EdgeLink на управление и запись экрана. Ключ подключения и настройки сохранятся. Приложение перезапустится; затем включите доступ для этой версии в настройках macOS."
        alert.addButton(withTitle: "Обновить разрешения"); alert.addButton(withTitle: "Отмена")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        stopLink()
        Task { @MainActor in
            do {
                try await Task.detached { try Permissions.resetOwnGrants() }.value
                state.tab = 1; state.step = 1; restart()
            } catch { state.permissionHint = "Не удалось обновить разрешения: \(error.localizedDescription). Удалите старую запись EdgeLink и добавьте приложение из Applications вручную." }
        }
    }
    private func restart() {
        guard !preview else { return }
        let path = Bundle.main.bundleURL.path
        let helper = Process(); helper.executableURL = URL(fileURLWithPath: "/bin/bash")
        helper.arguments = ["-c", "for i in $(/usr/bin/seq 1 100); do /bin/kill -0 \"$1\" 2>/dev/null || break; /bin/sleep 0.1; done; /usr/bin/open \"$2\"", "edgelink-restart", String(getpid()), path]
        do { try helper.run(); quit() } catch { state.permissionHint = error.localizedDescription }
    }
    @objc private func toggle() {
        guard !busy && !updating else { return }
        if running { stopLink(); return }
        guard Permissions.inputAllowed else { state.status = "Разрешите EdgeLink управление компьютером в настройках конфиденциальности macOS."; return }
        guard let secret else { state.status = "Ключ сопряжения недоступен."; return }
        input.display = state.selectedDisplay
        busy = true
        state.status = "Запуск захвата звука…"
        let ip = state.localIP.trimmingCharacters(in: .whitespacesAndNewlines)
        let peer = state.automatic ? "" : state.pcIP.trimmingCharacters(in: .whitespacesAndNewlines)
        Task { @MainActor in
            do {
                // Capture permission / format is resolved before accepting a Windows client.
                try await audio.start(ip: ip, displayID: input.display)
                try server.start(ip: ip, peerIP: peer, master: secret, displayWidth: input.bounds.width, displayHeight: input.bounds.height)
                try discovery.start(ip: ip, key: secret)
                boundIP = ip; running = true; state.audioAllowed = true
                UserDefaults.standard.set(ip, forKey: "localIP"); UserDefaults.standard.set(peer, forKey: "pcIP")
            } catch {
                server.stop(); discovery.stop(); await audio.stop(); state.status = "Ошибка: \(error.localizedDescription)"
                retryAfter = Date(timeIntervalSinceNow: 10)
            }
            busy = false
        }
    }
    @objc private func stopLink() {
        outputMute.update(connected: false, enabled: state.muteMac); state.audioHint = outputMute.hint
        automaticPaused = true; state.connected = false
        guard running && !busy else { return }
        running = false; busy = true
        server.stop(); discovery.stop()
        Task { @MainActor in
            await audio.stop(); busy = false; state.status = "Остановлено"
        }
    }
    @objc private func suspendLink() {
        let paused = automaticPaused; stopLink(); automaticPaused = paused
        retryAfter = Date.distantFuture
    }
    @objc private func resumeLink() { retryAfter = Date(); refreshSetup() }
    @objc private func quit() { outputMute.restore(); server.stop(); discovery.stop(); NSApp.terminate(nil) }
    func applicationWillTerminate(_ notification: Notification) { outputMute.restore(); updateTask?.cancel(); monitor?.invalidate(); server.stop(); discovery.stop() }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
