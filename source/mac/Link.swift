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
    private var listenFD: Int32 = -1
    private var controlFD: Int32 = -1
    private var inputFD: Int32 = -1
    private var session: Session?
    private var pendingMouse: (Data, Session)?
    private var mouseScheduled = false
    let input: InputReceiver
    var status: ((String) -> Void)?
    init(input: InputReceiver) { self.input = input }
    var currentSession: Session? { state.lock(); defer { state.unlock() }; return session }
    private var isRunning: Bool { state.lock(); defer { state.unlock() }; return running }
    func start(ip: String, peerIP: String, master: Data, displayWidth: Double, displayHeight: Double) throws {
        guard master.count == 32 else { throw LinkError.message("Invalid key") }
        let expectedPeer = peerIP.isEmpty ? nil : try Wire.address(peerIP, port: 0).sin_addr
        let listener = socket(AF_INET, SOCK_STREAM, 0), udp = socket(AF_INET, SOCK_DGRAM, 0)
        guard listener >= 0 && udp >= 0 else { if listener >= 0 { close(listener) }; if udp >= 0 { close(udp) }; throw LinkError.message("Socket error") }
        do {
            var one: Int32 = 1; setsockopt(listener, SOL_SOCKET, SO_REUSEADDR, &one, 4)
            try Wire.bind(listener, ip: ip, port: 48791); try Wire.bind(udp, ip: ip, port: 48793)
            Wire.timeout(udp, seconds: 1)
            var receiveTimeout = timeval(tv_sec: 0, tv_usec: 100000)
            setsockopt(udp, SOL_SOCKET, SO_RCVTIMEO, &receiveTimeout, socklen_t(MemoryLayout<timeval>.size))
            guard listen(listener, 2) == 0 else { throw LinkError.message("Listen failed") }
        } catch { close(listener); close(udp); throw error }
        state.lock(); running = true; listenFD = listener; inputFD = udp; state.unlock()
        workers.enter()
        DispatchQueue.global(qos: .userInteractive).async { [self] in
            defer { close(listener); state.lock(); if listenFD == listener { listenFD = -1 }; state.unlock(); workers.leave() }
            while isRunning {
                var address = sockaddr_in(), size = socklen_t(MemoryLayout<sockaddr_in>.size)
                let fd = withUnsafeMutablePointer(to: &address) { p in p.withMemoryRebound(to: sockaddr.self, capacity: 1) { accept(listener, $0, &size) } }
                guard fd >= 0 else { break }
                if let expectedPeer, address.sin_addr.s_addr != expectedPeer.s_addr { close(fd); continue }
                state.lock(); controlFD = fd; state.unlock()
                Wire.timeout(fd, seconds: 3)
                var one: Int32 = 1; setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, &one, 4)
                do {
                    let hello = try Wire.receive(fd, key: SymmetricKey(data: master))
                    guard hello["type"] as? String == "connect", hello["version"] as? Int == 1,
                          let nonceText = hello["nonce"] as? String, let clientNonce = Data(base64Encoded: nonceText), clientNonce.count == 16,
                          let audioPort = hello["audioPort"] as? Int, audioPort == 48792 else { throw LinkError.message("Неверный ключ или версия клиента") }
                    let serverNonce = Data((0..<16).map { _ in UInt8.random(in: 0...255) })
                    try Wire.send(fd, message: ["type": "hello", "version": 1, "nonce": serverNonce.base64EncodedString(), "width": displayWidth, "height": displayHeight], key: SymmetricKey(data: master))
                    let s = Session(key: Wire.sessionKey(master: master, client: clientNonce, server: serverNonce), peer: address.sin_addr, audioPort: UInt16(audioPort))
                    state.lock(); session = s; state.unlock(); report("ПК подключён · звук передаётся")
                    while isRunning {
                        let message = try Wire.receive(fd, key: s.key)
                        if message["type"] as? String == "ping" {
                            try Wire.send(fd, message: ["type": "pong", "time": message["time"] ?? 0], key: s.key)
                        } else {
                            DispatchQueue.main.async { [self] in if currentSession === s { input.handle(message) } }
                        }
                    }
                } catch { if isRunning { report("ПК отключён: \(error.localizedDescription)") } }
                state.lock(); session = nil; pendingMouse = nil; if controlFD == fd { controlFD = -1 }; state.unlock()
                shutdown(fd, SHUT_RDWR); close(fd)
                DispatchQueue.main.async { [self] in input.release() }
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
        state.lock(); running = false; session = nil; pendingMouse = nil
        if listenFD >= 0 { shutdown(listenFD, SHUT_RDWR) }
        if controlFD >= 0 { shutdown(controlFD, SHUT_RDWR) }
        state.unlock(); input.release()
        _ = workers.wait(timeout: .now() + 1)
    }
    private func report(_ text: String) { DispatchQueue.main.async { [weak self] in self?.status?(text) } }
}
