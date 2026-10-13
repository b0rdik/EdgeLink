import Foundation
import CryptoKit
import Darwin

final class Session {
    let key: SymmetricKey
    let peer: in_addr
    let audioTarget: sockaddr_in
    init(key: SymmetricKey, peer: in_addr, audioPort: UInt16) {
        self.key = key; self.peer = peer
        var a = sockaddr_in(); a.sin_len = UInt8(MemoryLayout<sockaddr_in>.size); a.sin_family = sa_family_t(AF_INET); a.sin_addr = peer; a.sin_port = audioPort.bigEndian
        audioTarget = a
    }
}

final class LinkServer {
    private let state = NSLock()
    private let workers = DispatchGroup()
    private var running = false
    private var generation = 0
    private var listenFD: Int32 = -1
    private var wakeFD: Int32 = -1
    private var controlFD: Int32 = -1
    private var inputFD: Int32 = -1
    private var session: Session?
    private var pendingMouse: (Data, Session)?
    private var mouseScheduled = false
    let input: InputReceiver
    private let acceptConnection: (Int32, UnsafeMutablePointer<sockaddr>?, UnsafeMutablePointer<socklen_t>?) -> Int32
    var status: ((String) -> Void)?
    var failed: ((String) -> Void)?
    var audioBatchFrames: (() -> Int)?
    init(input: InputReceiver, acceptConnection: @escaping (Int32, UnsafeMutablePointer<sockaddr>?, UnsafeMutablePointer<socklen_t>?) -> Int32 = { Darwin.accept($0, $1, $2) }) {
        self.input = input; self.acceptConnection = acceptConnection
    }
    var currentSession: Session? { state.lock(); defer { state.unlock() }; return session }
    private var isRunning: Bool { state.lock(); defer { state.unlock() }; return running }
    func start(ip: String, peerIP: String, master: Data, displayWidth: Double, displayHeight: Double) throws {
        guard workers.wait(timeout: .now()) == .success else { throw LinkError.message("Предыдущее соединение ещё завершается; повторим подключение автоматически.") }
        guard master.count == 32 else { throw LinkError.message("Invalid key") }
        let expectedPeer = peerIP.isEmpty ? nil : try Wire.address(peerIP, port: 0).sin_addr
        let listener = socket(AF_INET, SOCK_STREAM, 0), udp = socket(AF_INET, SOCK_DGRAM, 0)
        guard listener >= 0 && udp >= 0 else { if listener >= 0 { close(listener) }; if udp >= 0 { close(udp) }; throw LinkError.message("Socket error") }
        var wake = [Int32](repeating: -1, count: 2)
        guard wake.withUnsafeMutableBufferPointer({ Darwin.pipe($0.baseAddress!) }) == 0 else {
            close(listener); close(udp); throw LinkError.message("Не удалось создать сигнал остановки сервера")
        }
        let wakeRead = wake[0], wakeWrite = wake[1]
        // A self-pipe wakes poll even with no connected client. shutdown() on a
        // listening Darwin socket does not reliably interrupt a blocking accept.
        _ = fcntl(wakeWrite, F_SETFL, O_NONBLOCK)
        for fd in [listener, udp, wakeRead, wakeWrite] { _ = fcntl(fd, F_SETFD, FD_CLOEXEC) }
        do {
            var one: Int32 = 1; setsockopt(listener, SOL_SOCKET, SO_REUSEADDR, &one, 4)
            try Wire.bind(listener, ip: ip, port: 48791); try Wire.bind(udp, ip: ip, port: 48793)
            Wire.timeout(udp, seconds: 1)
            var receiveTimeout = timeval(tv_sec: 0, tv_usec: 100000)
            setsockopt(udp, SOL_SOCKET, SO_RCVTIMEO, &receiveTimeout, socklen_t(MemoryLayout<timeval>.size))
            guard listen(listener, 2) == 0 else { throw LinkError.message("Listen failed") }
        } catch { close(listener); close(udp); close(wakeRead); close(wakeWrite); throw error }
        state.lock(); generation &+= 1; let run = generation; running = true; listenFD = listener; wakeFD = wakeWrite; inputFD = udp; state.unlock()
        workers.enter()
        DispatchQueue.global(qos: .userInteractive).async { [self] in
            defer {
                state.lock(); if listenFD == listener { listenFD = -1 }; if wakeFD == wakeWrite { wakeFD = -1 }; state.unlock()
                close(listener); close(wakeRead); close(wakeWrite); workers.leave()
            }
            while isRunning {
                var events = [pollfd(fd: listener, events: Int16(POLLIN), revents: 0), pollfd(fd: wakeRead, events: Int16(POLLIN), revents: 0)]
                let ready = events.withUnsafeMutableBufferPointer { Darwin.poll($0.baseAddress!, nfds_t($0.count), -1) }
                if ready < 0 && errno == EINTR { continue }
                guard isRunning else { break }
                if events[1].revents != 0 { break }
                if ready < 0 || events[0].revents & Int16(POLLIN) == 0 {
                    reportFailure("Ожидание подключения прервано; сервер будет перезапущен.", run: run)
                    break
                }
                var address = sockaddr_in(), size = socklen_t(MemoryLayout<sockaddr_in>.size)
                let fd = withUnsafeMutablePointer(to: &address) { p in p.withMemoryRebound(to: sockaddr.self, capacity: 1) { acceptConnection(listener, $0, &size) } }
                if fd < 0 {
                    if errno == EINTR { continue }
                    if isRunning {
                        let error = "Сервер соединения остановлен: \(String(cString: strerror(errno)))"
                        reportFailure(error, run: run)
                    }
                    break
                }
                if let expectedPeer, address.sin_addr.s_addr != expectedPeer.s_addr { close(fd); continue }
                _ = fcntl(fd, F_SETFD, FD_CLOEXEC)
                state.lock()
                guard running else { state.unlock(); close(fd); break }
                controlFD = fd; state.unlock()
                Wire.timeout(fd, seconds: 3)
                var one: Int32 = 1; setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, &one, 4)
                do {
                    let hello = try Wire.receive(fd, key: SymmetricKey(data: master))
                    guard hello["type"] as? String == "connect", hello["version"] as? Int == 1,
                          let nonceText = hello["nonce"] as? String, let clientNonce = Data(base64Encoded: nonceText), clientNonce.count == 16,
                          let audioPort = hello["audioPort"] as? Int, audioPort == 48792 else { throw LinkError.message("Неверный ключ или версия клиента") }
                    let serverNonce = Data((0..<16).map { _ in UInt8.random(in: 0...255) })
                    try Wire.send(fd, message: ["type": "hello", "version": 1, "nonce": serverNonce.base64EncodedString(), "width": displayWidth, "height": displayHeight, "audioBatchFrames": audioBatchFrames?() ?? 0], key: SymmetricKey(data: master))
                    let s = Session(key: Wire.sessionKey(master: master, client: clientNonce, server: serverNonce), peer: address.sin_addr, audioPort: UInt16(audioPort))
                    state.lock(); session = s; state.unlock(); report("ПК подключён · звук передаётся")
                    while isRunning {
                        let message = try Wire.receive(fd, key: s.key)
                        if message["type"] as? String == "ping" {
                            try Wire.send(fd, message: ["type": "pong", "time": message["time"] ?? 0, "audioBatchFrames": audioBatchFrames?() ?? 0], key: s.key)
                        } else {
                            DispatchQueue.main.async { [self] in if currentSession === s { input.handle(message) } }
                        }
                    }
                } catch { if isRunning { report("ПК отключён: \(error.localizedDescription)") } }
                state.lock(); session = nil; pendingMouse = nil; if controlFD == fd { controlFD = -1 }; state.unlock()
                shutdown(fd, SHUT_RDWR); close(fd)
                DispatchQueue.main.async { [self] in if currentSession == nil { input.release() } }
            }
        }
        workers.enter()
        DispatchQueue.global(qos: .userInteractive).async { [self] in
            defer { close(udp); state.lock(); if inputFD == udp { inputFD = -1 }; state.unlock(); workers.leave() }
            var buffer = [UInt8](repeating: 0, count: 2048)
            while isRunning {
                var address = sockaddr_in(), size = socklen_t(MemoryLayout<sockaddr_in>.size)
                let n = buffer.withUnsafeMutableBytes { bytes in
                    withUnsafeMutablePointer(to: &address) { p in p.withMemoryRebound(to: sockaddr.self, capacity: 1) { recvfrom(udp, bytes.baseAddress, bytes.count, 0, $0, &size) } }
                }
                guard n == 57, let s = currentSession, address.sin_addr.s_addr == s.peer.s_addr,
                      let plain = try? Wire.open(Data(buffer.prefix(n)), key: s.key) else { continue }
                state.lock(); pendingMouse = (plain, s)
                let schedule = !mouseScheduled; mouseScheduled = true; state.unlock()
                if schedule {
                    DispatchQueue.main.async { [self] in
                        state.lock(); let move = pendingMouse; pendingMouse = nil; mouseScheduled = false; let live = session; state.unlock()
                        if let move, live === move.1 { input.udp(move.0) }
                    }
                }
            }
        }
        report("Ожидание ПК на \(ip):48791")
    }
    func stop() {
        state.lock(); generation &+= 1; running = false; session = nil; pendingMouse = nil
        if wakeFD >= 0 { var byte: UInt8 = 1; _ = Darwin.write(wakeFD, &byte, 1) }
        if controlFD >= 0 { shutdown(controlFD, SHUT_RDWR) }
        state.unlock(); input.release()
        _ = workers.wait(timeout: .now() + 1)
    }
    private func reportFailure(_ text: String, run: Int) {
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            state.lock(); let live = running && generation == run; state.unlock()
            if live { failed?(text) }
        }
    }
    private func report(_ text: String) { DispatchQueue.main.async { [weak self] in self?.status?(text) } }
}
