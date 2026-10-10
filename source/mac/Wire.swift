import Foundation
import CryptoKit
import Darwin

enum LinkError: Error, LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let value) = self { return value }; return nil }
}

enum Wire {
    static func seal(_ data: Data, key: SymmetricKey) throws -> Data {
        guard let packet = try AES.GCM.seal(data, using: key).combined else { throw LinkError.message("Encryption failed") }
        return packet
    }
    static func open(_ packet: Data, key: SymmetricKey) throws -> Data {
        guard packet.count >= 28 && packet.count <= 16384 else { throw LinkError.message("Invalid packet length") }
        return try AES.GCM.open(AES.GCM.SealedBox(combined: packet), using: key)
    }
    static func sessionKey(master: Data, client: Data, server: Data) -> SymmetricKey {
        SymmetricKey(data: Data(SHA256.hash(data: master + client + server + Data("EdgeLink-v1".utf8))))
    }
    static func json(_ value: [String: Any]) throws -> Data { try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys]) }
    static func receive(_ fd: Int32, key: SymmetricKey) throws -> [String: Any] {
        let header = try read(fd, count: 4)
        let length = header.reduce(0) { ($0 << 8) | Int($1) }
        guard length >= 28 && length <= 16384 else { throw LinkError.message("Invalid frame length") }
        let packet = try read(fd, count: length)
        guard let message = try JSONSerialization.jsonObject(with: open(packet, key: key)) as? [String: Any] else { throw LinkError.message("Invalid message") }
        return message
    }
    static func send(_ fd: Int32, message: [String: Any], key: SymmetricKey) throws {
        let packet = try seal(json(message), key: key)
        var length = UInt32(packet.count).bigEndian
        let header = withUnsafeBytes(of: &length) { Data($0) }
        try write(fd, data: header + packet)
    }
    static func read(_ fd: Int32, count: Int) throws -> Data {
        var data = Data(count: count), received = 0
        try data.withUnsafeMutableBytes { raw in
            while received < count {
                let n = Darwin.recv(fd, raw.baseAddress!.advanced(by: received), count - received, 0)
                if n < 0 && errno == EINTR { continue }
                guard n > 0 else { throw LinkError.message("Соединение закрыто или истекло время ожидания") }
                received += n
            }
        }
        return data
    }
    static func write(_ fd: Int32, data: Data) throws {
        try data.withUnsafeBytes { raw in
            var sent = 0
            while sent < data.count {
                let n = Darwin.send(fd, raw.baseAddress!.advanced(by: sent), data.count - sent, 0)
                if n < 0 && errno == EINTR { continue }
                guard n > 0 else { throw LinkError.message("Ошибка отправки") }
                sent += n
            }
        }
    }
    static func address(_ ip: String, port: UInt16) throws -> sockaddr_in {
        var result = sockaddr_in()
        result.sin_len = UInt8(MemoryLayout<sockaddr_in>.size); result.sin_family = sa_family_t(AF_INET); result.sin_port = port.bigEndian
        guard ip.withCString({ inet_pton(AF_INET, $0, &result.sin_addr) }) == 1 else { throw LinkError.message("Неверный IPv4-адрес") }
        return result
    }
    static func bind(_ fd: Int32, ip: String, port: UInt16) throws {
        var a = try address(ip, port: port)
        let result = withUnsafePointer(to: &a) { p in p.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
        guard result == 0 else { throw LinkError.message("Не удалось открыть \(ip):\(port): \(String(cString: strerror(errno)))") }
    }
    static func sendUDP(_ fd: Int32, data: Data, to: sockaddr_in) -> Bool {
        var address = to
        return data.withUnsafeBytes { bytes in
            withUnsafePointer(to: &address) { pointer in
                pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.sendto(fd, bytes.baseAddress, data.count, MSG_DONTWAIT, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) == data.count }
            }
        }
    }
    static func timeout(_ fd: Int32, seconds: Int) {
        var t = timeval(tv_sec: seconds, tv_usec: 0)
        setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &t, socklen_t(MemoryLayout<timeval>.size))
        setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &t, socklen_t(MemoryLayout<timeval>.size))
        var one: Int32 = 1; setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &one, 4)
    }
}

extension Data {
    func little<T: FixedWidthInteger>(_ offset: Int, as: T.Type = T.self) -> T {
        self.withUnsafeBytes { T(littleEndian: $0.loadUnaligned(fromByteOffset: offset, as: T.self)) }
    }
    mutating func appendLE<T: FixedWidthInteger>(_ value: T) {
        var v = value.littleEndian; Swift.withUnsafeBytes(of: &v) { append(contentsOf: $0) }
    }
}
