import Foundation
import CoreAudio
import Darwin

struct MuteDevice: Codable, Equatable {
    let id: UInt32
    let uid: String
}

protocol OutputMuteBackend {
    func defaultDevice() -> MuteDevice?
    func device(uid: String) -> MuteDevice?
    func muted(_ device: MuteDevice) -> Bool?
    func setMuted(_ value: Bool, on device: MuteDevice) -> Bool
}

struct CoreAudioMute: OutputMuteBackend {
    private func address(_ selector: AudioObjectPropertySelector, output: Bool = false) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: output ? kAudioDevicePropertyScopeOutput : kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
    }
    private func identity(_ id: AudioDeviceID) -> MuteDevice? {
        var a = address(kAudioDevicePropertyDeviceUID), uid: CFString?
        var size = UInt32(MemoryLayout<CFString?>.size)
        let result = withUnsafeMutablePointer(to: &uid) { AudioObjectGetPropertyData(id, &a, 0, nil, &size, $0) }
        guard result == noErr, let uid else { return nil }
        return MuteDevice(id: id, uid: uid as String)
    }
    func defaultDevice() -> MuteDevice? {
        var a = address(kAudioHardwarePropertyDefaultOutputDevice), id = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, &id) == noErr else { return nil }
        return identity(id)
    }
    func device(uid: String) -> MuteDevice? {
        var a = address(kAudioHardwarePropertyDevices), size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size) == noErr else { return nil }
        var ids = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        guard !ids.isEmpty else { return nil }
        let result = ids.withUnsafeMutableBytes { AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, $0.baseAddress!) }
        guard result == noErr else { return nil }
        return ids.compactMap { identity($0) }.first { $0.uid == uid }
    }
    func muted(_ device: MuteDevice) -> Bool? {
        guard identity(device.id)?.uid == device.uid else { return nil }
        var a = address(kAudioDevicePropertyMute, output: true), value: UInt32 = 0, size: UInt32 = 4
        guard AudioObjectGetPropertyData(device.id, &a, 0, nil, &size, &value) == noErr else { return nil }
        return value != 0
    }
    func setMuted(_ value: Bool, on device: MuteDevice) -> Bool {
        guard identity(device.id)?.uid == device.uid else { return false }
        var a = address(kAudioDevicePropertyMute, output: true), settable = DarwinBoolean(false), raw: UInt32 = value ? 1 : 0
        guard AudioObjectIsPropertySettable(device.id, &a, &settable) == noErr, settable.boolValue else { return false }
        return AudioObjectSetPropertyData(device.id, &a, 0, nil, 4, &raw) == noErr
    }
}

// Called on the main thread. Only restores mute states changed by this app;
// output volume is never modified. The journal survives an abnormal exit.
final class OutputMute {
    private struct Record: Codable { let device: MuteDevice; let pid: Int32 }
    private let backend: OutputMuteBackend
    private let defaults: UserDefaults
    private let journalKey = "outputMuteRecovery"
    private var owned: MuteDevice?
    private var attempted: MuteDevice?
    private(set) var hint = "Динамики Mac работают обычно."
    init(backend: OutputMuteBackend = CoreAudioMute(), defaults: UserDefaults = .standard) {
        self.backend = backend; self.defaults = defaults
    }
    func recover() {
        guard let data = defaults.data(forKey: journalKey), let record = try? JSONDecoder().decode(Record.self, from: data) else { return }
        // A second app instance must not undo a live instance's mute lease.
        if record.pid != getpid(), record.pid > 0, kill(record.pid, 0) == 0 { return }
        guard let device = backend.device(uid: record.device.uid) else { return }
        if backend.muted(device) == false || backend.setMuted(false, on: device) { defaults.removeObject(forKey: journalKey) }
    }
    func update(connected: Bool, enabled: Bool) {
        let device = connected && enabled ? backend.defaultDevice() : nil
        if owned != nil && (owned != device || !connected || !enabled) { restore() }
        guard connected && enabled, let device else {
            attempted = nil
            if owned == nil { hint = "Динамики Mac работают обычно." }
            return
        }
        if owned == device {
            if backend.muted(device) == false {
                owned = nil; defaults.removeObject(forKey: journalKey)
                hint = "Звук Mac включён вручную."
            }
            return
        }
        guard owned == nil, attempted != device else { return }
        attempted = device
        guard let muted = backend.muted(device) else { hint = "Этот аудиовыход не поддерживает отключение звука. Выключите его вручную."; return }
        if muted { hint = "Звук Mac уже был выключен."; return }
        let record = Record(device: device, pid: getpid())
        guard let data = try? JSONEncoder().encode(record) else { return }
        defaults.set(data, forKey: journalKey)
        if backend.setMuted(true, on: device) {
            owned = device; hint = "Динамики Mac выключены до отключения ПК."
        } else {
            defaults.removeObject(forKey: journalKey); hint = "Не удалось выключить этот аудиовыход. Выключите его вручную."
        }
    }
    func restore() {
        guard let device = owned else { return }
        if backend.muted(device) == false || backend.setMuted(false, on: device) {
            owned = nil; defaults.removeObject(forKey: journalKey)
        } else { hint = "Не удалось вернуть звук Mac. Включите динамики вручную." }
    }
}
