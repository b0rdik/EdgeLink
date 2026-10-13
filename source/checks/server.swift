import AppKit
import CryptoKit
import Foundation

let master = Data((0..<32).map { UInt8($0) })
let key = SymmetricKey(data: master)
let value = Data("Test Δ русский".utf8)
let opened = try Wire.open(Wire.seal(value, key: key), key: key)
precondition(opened == value)
let nist = Data([UInt8](repeating: 0, count: 12) + [0x53,0x0f,0x8a,0xfb,0xc7,0x45,0x36,0xb9,0xa9,0x63,0xb4,0xf1,0xc4,0xcb,0x73,0x8b])
let known = try Wire.open(nist, key: SymmetricKey(data: Data(repeating: 0, count: 32)))
precondition(known.isEmpty)
print("PASS: Swift AES-GCM / known vector")
func check(_ value: Bool, _ label: String) { precondition(value, label); print("PASS: " + label) }
var pcmBuffer = Data(repeating: 0xaa, count: 961)
pcmBuffer.removeFirst()
check(pcmBuffer.startIndex > 0, "PCM regression exercises nonzero Data startIndex")
check(AudioPCM.takePacket(from: &pcmBuffer) == Data(repeating: 0xaa, count: 960), "PCM packet from sliced Data")
var expectedPCM = Data(), receivedPCM = Data()
for cycle in 0..<10000 {
    let chunk = Data((0..<(cycle % 2049)).map { UInt8(truncatingIfNeeded: $0 + cycle) })
    pcmBuffer.append(chunk); expectedPCM.append(chunk)
    while let packet = AudioPCM.takePacket(from: &pcmBuffer) { receivedPCM.append(packet) }
    precondition(pcmBuffer.count < AudioPCM.packetBytes)
}
receivedPCM.append(pcmBuffer)
check(receivedPCM == expectedPCM, "PCM 10000 variable capture batches preserve every byte and partial tail")
pcmBuffer.removeAll(keepingCapacity: true)
check(AudioPCM.takePacket(from: &pcmBuffer) == nil, "PCM empty/reset buffer waits for a full packet")
final class FakeOutputMute: OutputMuteBackend {
    let first = MuteDevice(id: 1, uid: "first"), second = MuteDevice(id: 2, uid: "second")
    var selected = 1, states: [UInt32: Bool] = [1: false, 2: false], writes = 0, writable = true
    func defaultDevice() -> MuteDevice? { selected == 1 ? first : second }
    func device(uid: String) -> MuteDevice? { uid == first.uid ? first : uid == second.uid ? second : nil }
    func muted(_ device: MuteDevice) -> Bool? { states[device.id] }
    func setMuted(_ value: Bool, on device: MuteDevice) -> Bool { guard writable else { return false }; states[device.id] = value; writes += 1; return true }
}
let muteSuite = "EdgeLink-mute-check-" + UUID().uuidString
let muteDefaults = UserDefaults(suiteName: muteSuite)!
let muteBackend = FakeOutputMute(), mute = OutputMute(backend: muteBackend, defaults: muteDefaults)
mute.update(connected: false, enabled: true)
check(muteBackend.writes == 0, "waiting for PC does not mute Mac")
mute.update(connected: true, enabled: true)
check(muteBackend.states[1] == true, "connected PC acquires local output mute")
muteBackend.selected = 2; mute.update(connected: true, enabled: true)
check(muteBackend.states[1] == false && muteBackend.states[2] == true, "output switch restores old device and mutes new device")
mute.update(connected: false, enabled: true)
check(muteBackend.states[2] == false, "disconnect restores app-owned mute")
muteBackend.states[2] = true; let beforeMuted = muteBackend.writes
mute.update(connected: true, enabled: true); mute.update(connected: false, enabled: true)
check(muteBackend.writes == beforeMuted && muteBackend.states[2] == true, "previously muted device stays muted")
muteBackend.states[2] = false; mute.update(connected: true, enabled: true)
muteBackend.states[2] = false; let beforeManual = muteBackend.writes
mute.update(connected: true, enabled: true); mute.update(connected: true, enabled: true)
check(muteBackend.writes == beforeManual, "manual unmute is respected during current connection")
mute.update(connected: false, enabled: true); mute.update(connected: true, enabled: true)
let recovered = OutputMute(backend: muteBackend, defaults: muteDefaults); recovered.recover()
check(muteBackend.states[2] == false && muteDefaults.data(forKey: "outputMuteRecovery") == nil, "abnormal exit journal restores only recorded device")
muteBackend.writable = false
let unsupported = OutputMute(backend: muteBackend, defaults: muteDefaults); unsupported.update(connected: true, enabled: true)
check(muteBackend.states[2] == false && muteDefaults.data(forKey: "outputMuteRecovery") == nil, "failed mute leaves no recovery record or changed state")
muteDefaults.removePersistentDomain(forName: muteSuite)
func rejects(_ label: String, _ work: () throws -> Void) { do { try work(); fatalError("FAIL: " + label) } catch { print("PASS: " + label) } }
let release: [String: Any] = ["tag_name": "v0.2.0", "draft": false, "prerelease": false, "assets": [["name": Updates.asset, "browser_download_url": "https://github.com/b0rdik/EdgeLink/releases/download/v0.2.0/" + Updates.asset, "digest": "sha256:" + String(repeating: "a", count: 64), "size": 1234]]]
let metadata = try Wire.json(release)
let update = try Updates.parse(metadata, current: "0.1.0")
check(update?.version.description == "0.2.0", "Mac release / SHA metadata parsed")
let same = try Updates.parse(metadata, current: "0.2.0"), old = try Updates.parse(metadata, current: "1.0.0")
check(same == nil && old == nil, "Mac updater skips same version / downgrade")
for invalid in ["v01.2.0", "0.2.0-beta", "0.2", "0.2.0/evil"] { check(ReleaseVersion(invalid) == nil, "Mac rejects invalid version " + invalid) }
for bad in ["/EdgeLink.app/x", "EdgeLink.app/../escape", "EdgeLink.app/x\\y", "Other.app/x"] { check(!Updates.safeEntry(bad), "Mac rejects unsafe ZIP " + bad) }
check(Updates.safeEntry("EdgeLink.app/Contents/MacOS/EdgeLink"), "Mac permits expected bundle path")
check(!UpdateRedirects.allowed(URL(string: "https://github.com.evil.invalid/x")!), "Mac rejects unrelated download host")
var badRelease = release; badRelease["assets"] = [["name": Updates.asset, "browser_download_url": "https://evil.invalid/app.zip", "digest": "sha256:" + String(repeating: "a", count: 64), "size": 1234]]
rejects("Mac rejects altered release URL") { _ = try Updates.parse(Wire.json(badRelease), current: "0.1.0") }
if let path = ProcessInfo.processInfo.environment["INSTALLER_SCRIPT_EXPORT"] { try Updates.installerScript.write(toFile: path, atomically: true, encoding: .utf8) }
let checkDiscovery = ProcessInfo.processInfo.environment["EDGELINK_SKIP_DISCOVERY_CHECK"] != "1"
let discovery = DiscoveryServer(); if checkDiscovery { try discovery.start(ip: "127.0.0.1", key: master) }
let input = InputReceiver()
let idleServer = LinkServer(input: input)
for _ in 0..<10 {
    try idleServer.start(ip: "127.0.0.1", peerIP: "127.0.0.1", master: master, displayWidth: 1920, displayHeight: 1080)
    Thread.sleep(forTimeInterval: 0.15)
    idleServer.stop()
}
print("PASS: idle listener stop / restart releases ports in 10 cycles without any client")
var interruptNextAccept = true
let server = LinkServer(input: input, acceptConnection: { fd, address, size in
    if interruptNextAccept { interruptNextAccept = false; errno = EINTR; print("PASS: injecting an interrupted accept before the real handshake"); return -1 }
    return Darwin.accept(fd, address, size)
})
server.audioBatchFrames = { 1024 }
server.status = { print($0); fflush(stdout) }
try server.start(ip: "127.0.0.1", peerIP: "127.0.0.1", master: master, displayWidth: 1920, displayHeight: 1080)
let udp = socket(AF_INET, SOCK_DGRAM, 0)
var sequence: UInt64 = 0, frame: UInt64 = 0
let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .userInteractive))
timer.schedule(deadline: .now(), repeating: .milliseconds(5))
timer.setEventHandler {
    guard let session = server.currentSession else { return }
    sequence += 1
    var plain = Data([2]); plain.appendLE(sequence); plain.appendLE(frame)
    for _ in 0..<480 { plain.appendLE(Int16(1000)) }
    frame += 240
    if let data = try? Wire.seal(plain, key: session.key) { _ = Wire.sendUDP(udp, data: data, to: session.audioTarget) }
}
timer.resume()
print("READY"); fflush(stdout)
DispatchQueue.main.asyncAfter(deadline: .now() + 3) {
    server.stop(); discovery.stop()
    do {
        if checkDiscovery { try discovery.start(ip: "127.0.0.1", key: master); discovery.stop() }
        try server.start(ip: "127.0.0.1", peerIP: "127.0.0.1", master: master, displayWidth: 1920, displayHeight: 1080)
        print("PASS: server stop / immediate restart releases ports")
        server.stop()
    } catch { print("FAIL: server restart \(error)"); exit(1) }
}
RunLoop.main.run(until: Date(timeIntervalSinceNow: 5))
server.stop(); discovery.stop(); close(udp)
