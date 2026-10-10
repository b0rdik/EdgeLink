import Foundation
import SystemConfiguration
import Darwin
import CryptoKit

struct EthernetAddress: Identifiable, Equatable {
    let id: String, name: String, ip: String
}
enum Ethernet {
    static func addresses() -> [EthernetAddress] {
        var names: [String: String] = [:]
        for interface in (SCNetworkInterfaceCopyAll() as? [SCNetworkInterface] ?? []) {
            if SCNetworkInterfaceGetInterfaceType(interface) as String? == kSCNetworkInterfaceTypeEthernet as String,
               let bsd = SCNetworkInterfaceGetBSDName(interface) as String? {
                names[bsd] = SCNetworkInterfaceGetLocalizedDisplayName(interface) as String? ?? bsd
            }
        }
        var pointer: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&pointer) == 0, let head = pointer else { return [] }
        defer { freeifaddrs(head) }
        var result: [EthernetAddress] = [], node: UnsafeMutablePointer<ifaddrs>? = head
        while let entry = node {
            defer { node = entry.pointee.ifa_next }
            let info = entry.pointee, bsd = String(cString: info.ifa_name)
            guard let name = names[bsd], info.ifa_flags & UInt32(IFF_UP) != 0, let address = info.ifa_addr,
                  address.pointee.sa_family == AF_INET else { continue }
            var value = address.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr }
            var text = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
            guard inet_ntop(AF_INET, &value, &text, socklen_t(text.count)) != nil else { continue }
            let ip = String(cString: text)
            result.append(EthernetAddress(id: bsd + "/" + ip, name: name, ip: ip))
        }
        return result.sorted { a, b in a.ip.hasPrefix("169.254.") != b.ip.hasPrefix("169.254.") ? a.ip.hasPrefix("169.254.") : a.id < b.id }
    }
}

final class DiscoveryServer {
    private let lock = NSLock()
    private var active = false
    private let workers = DispatchGroup()
    func start(ip: String, key: Data) throws {
        let fd = socket(AF_INET, SOCK_DGRAM, 0)
        guard fd >= 0 else { throw LinkError.message("Discovery socket error") }
        do { try Wire.bind(fd, ip: "0.0.0.0", port: 48794) } catch { close(fd); throw error }
        let local: UInt32
        do { local = try Wire.address(ip, port: 0).sin_addr.s_addr } catch { close(fd); throw error }
        var interfaceList: UnsafeMutablePointer<ifaddrs>?
        var interfaceIndex: UInt32 = 0
        var mask: UInt32 = UInt32(0xffff0000).bigEndian
        if getifaddrs(&interfaceList) == 0 {
            var node = interfaceList
            while let entry = node {
                defer { node = entry.pointee.ifa_next }
                if let a = entry.pointee.ifa_addr, a.pointee.sa_family == AF_INET,
                   a.withMemoryRebound(to: sockaddr_in.self, capacity: 1, { $0.pointee.sin_addr.s_addr }) == local,
                   let m = entry.pointee.ifa_netmask {
                    interfaceIndex = if_nametoindex(entry.pointee.ifa_name)
                    mask = m.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr.s_addr }; break
                }
            }
            freeifaddrs(interfaceList)
        }
        guard interfaceIndex != 0, setsockopt(fd, IPPROTO_IP, IP_BOUND_IF, &interfaceIndex, socklen_t(MemoryLayout<UInt32>.size)) == 0 else { close(fd); throw LinkError.message("Не удалось выбрать Ethernet для обнаружения") }
        let subnetMask = mask
        var timeout = timeval(tv_sec: 0, tv_usec: 100000)
        setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
        let fingerprint = SHA256.hash(data: key).prefix(8).map { String(format: "%02x", $0) }.joined()
        lock.lock(); active = true; lock.unlock(); workers.enter()
        DispatchQueue.global(qos: .utility).async { [self] in
            defer { close(fd); workers.leave() }
            var buffer = [UInt8](repeating: 0, count: 1024)
            while isActive {
                var address = sockaddr_in(), size = socklen_t(MemoryLayout<sockaddr_in>.size)
                let count = buffer.withUnsafeMutableBytes { bytes in withUnsafeMutablePointer(to: &address) { p in p.withMemoryRebound(to: sockaddr.self, capacity: 1) { recvfrom(fd, bytes.baseAddress, bytes.count, 0, $0, &size) } } }
                guard count > 0, (address.sin_addr.s_addr & subnetMask) == (local & subnetMask),
                      let request = try? JSONSerialization.jsonObject(with: Data(buffer.prefix(count))) as? [String: Any],
                      request["type"] as? String == "edgelink-discover", let nonce = request["nonce"] as? String, nonce.count == 32,
                      let reply = try? Wire.json(["type": "edgelink-mac", "protocol": 1, "nonce": nonce, "ip": ip,
                           "name": Host.current().localizedName ?? "Mac", "fingerprint": fingerprint]) else { continue }
                _ = Wire.sendUDP(fd, data: reply, to: address)
            }
        }
    }
    private var isActive: Bool { lock.lock(); defer { lock.unlock() }; return active }
    func stop() { lock.lock(); active = false; lock.unlock(); _ = workers.wait(timeout: .now() + 1) }
}
