import AppKit
import CoreGraphics

// All state and CGEvent injection live on the main queue.
final class InputReceiver {
    var display: CGDirectDisplayID = CGMainDisplayID()
    private var epoch: UInt32 = 0
    private var active = false
    var changed: ((Bool) -> Void)?
    private var sequence: UInt64 = 0
    private var heldKeys = Set<CGKeyCode>()
    private var heldButtons = Set<Int>()
    private var position = CGPoint.zero
    private var lastClick: (button: Int, time: TimeInterval, point: CGPoint, count: Int)?
    private let source = CGEventSource(stateID: .privateState)
    var bounds: CGRect { CGDisplayBounds(display) }

    func handle(_ message: [String: Any]) {
        guard let type = message["type"] as? String else { return }
        if type == "enter" {
            release(); epoch = UInt32(truncatingIfNeeded: (message["epoch"] as? NSNumber)?.uint64Value ?? 0); active = true; sequence = 0
            move(x: message["x"] as? Double ?? 0, y: message["y"] as? Double ?? 0); changed?(true); return
        }
        guard active, (message["epoch"] as? NSNumber)?.uint32Value == epoch else { return }
        if type == "leave" { release(); return }
        if type == "key", let scan = message["scan"] as? Int, let down = message["down"] as? Bool {
            guard let key = keycode(scan, swap: message["swap"] as? Bool ?? true) else { return }
            let repeated = down && heldKeys.contains(key)
            if down { heldKeys.insert(key) } else { heldKeys.remove(key) }
            let event = CGEvent(keyboardEventSource: source, virtualKey: key, keyDown: down)
            event?.setIntegerValueField(.keyboardEventAutorepeat, value: repeated ? 1 : 0)
            event?.flags = flags; event?.post(tap: .cghidEventTap)
        } else if type == "button", let button = message["button"] as? Int, let down = message["down"] as? Bool, (0...4).contains(button) {
            sequence = max(sequence, (message["positionSequence"] as? NSNumber)?.uint64Value ?? 0)
            move(x: message["x"] as? Double ?? 0, y: message["y"] as? Double ?? 0)
            let cgButton = CGMouseButton(rawValue: UInt32(button))!
            let eventType: CGEventType = button == 0 ? (down ? .leftMouseDown : .leftMouseUp) : button == 1 ? (down ? .rightMouseDown : .rightMouseUp) : (down ? .otherMouseDown : .otherMouseUp)
            if down { heldButtons.insert(button) } else { heldButtons.remove(button) }
            let event = CGEvent(mouseEventSource: source, mouseType: eventType, mouseCursorPosition: position, mouseButton: cgButton)
            if down {
                let now = ProcessInfo.processInfo.systemUptime
                let prior = lastClick
                let nearby = prior.map { hypot(position.x - $0.point.x, position.y - $0.point.y) < 5 } ?? false
                let count = prior != nil && prior!.button == button && now - prior!.time <= NSEvent.doubleClickInterval && nearby ? prior!.count + 1 : 1
                lastClick = (button, now, position, count)
                event?.setIntegerValueField(.mouseEventClickState, value: Int64(count))
            } else if let prior = lastClick, prior.button == button {
                event?.setIntegerValueField(.mouseEventClickState, value: Int64(prior.count))
            }
            event?.flags = flags; event?.post(tap: .cghidEventTap)
        } else if type == "scroll", let delta = message["delta"] as? Int {
            sequence = max(sequence, (message["positionSequence"] as? NSNumber)?.uint64Value ?? 0)
            move(x: message["x"] as? Double ?? 0, y: message["y"] as? Double ?? 0)
            let value = Int32(clamping: delta / 120)
            let horizontal = message["horizontal"] as? Bool ?? false
            let event = CGEvent(scrollWheelEvent2Source: source, units: .line, wheelCount: 2, wheel1: horizontal ? 0 : value, wheel2: horizontal ? value : 0, wheel3: 0)
            event?.flags = flags; event?.post(tap: .cghidEventTap)
        }
    }
    func udp(_ packet: Data) {
        guard packet.count == 29, packet[0] == 1, active, packet.little(1, as: UInt32.self) == epoch else { return }
        let s = packet.little(5, as: UInt64.self); guard s > sequence else { return }; sequence = s
        move(x: Double(bitPattern: packet.little(13, as: UInt64.self)), y: Double(bitPattern: packet.little(21, as: UInt64.self)))
    }
    private func move(x: Double, y: Double) {
        guard x.isFinite && y.isFinite else { return }
        let rect = bounds
        position = CGPoint(x: rect.minX + min(max(x, 0), rect.width - 1), y: rect.minY + min(max(y, 0), rect.height - 1))
        let button = heldButtons.contains(0) ? 0 : heldButtons.contains(1) ? 1 : heldButtons.sorted().first
        let type: CGEventType = button == nil ? .mouseMoved : button == 0 ? .leftMouseDragged : button == 1 ? .rightMouseDragged : .otherMouseDragged
        let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: position, mouseButton: CGMouseButton(rawValue: UInt32(button ?? 0))!)
        event?.flags = flags; event?.post(tap: .cghidEventTap)
    }
    func release() {
        for key in heldKeys {
            let event = CGEvent(keyboardEventSource: source, virtualKey: key, keyDown: false)
            event?.flags = []; event?.post(tap: .cghidEventTap)
        }
        for button in heldButtons {
            let type: CGEventType = button == 0 ? .leftMouseUp : button == 1 ? .rightMouseUp : .otherMouseUp
            CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: position, mouseButton: CGMouseButton(rawValue: UInt32(button))!)?.post(tap: .cghidEventTap)
        }
        heldKeys.removeAll(); heldButtons.removeAll(); active = false; changed?(false)
    }
    private var flags: CGEventFlags {
        var f: CGEventFlags = []
        if heldKeys.contains(55) || heldKeys.contains(54) { f.insert(.maskCommand) }
        if heldKeys.contains(59) || heldKeys.contains(62) { f.insert(.maskControl) }
        if heldKeys.contains(58) || heldKeys.contains(61) { f.insert(.maskAlternate) }
        if heldKeys.contains(56) || heldKeys.contains(60) { f.insert(.maskShift) }
        return f
    }
    private func keycode(_ scan: Int, swap: Bool) -> CGKeyCode? {
        if scan == 0x1d { return swap ? 55 : 59 }
        if scan == 0x11d { return swap ? 54 : 62 }
        if scan == 0x15b { return swap ? 59 : 55 }
        if scan == 0x15c { return swap ? 62 : 54 }
        let keys: [Int: CGKeyCode] = [
            1:53, 2:18, 3:19, 4:20, 5:21, 6:23, 7:22, 8:26, 9:28, 10:25, 11:29, 12:27, 13:24, 14:51, 15:48,
            16:12, 17:13, 18:14, 19:15, 20:17, 21:16, 22:32, 23:34, 24:31, 25:35, 26:33, 27:30, 28:36,
            30:0, 31:1, 32:2, 33:3, 34:5, 35:4, 36:38, 37:40, 38:37, 39:41, 40:39, 41:50, 42:56, 43:42,
            44:6, 45:7, 46:8, 47:9, 48:11, 49:45, 50:46, 51:43, 52:47, 53:44, 54:60, 55:67, 56:58, 57:49, 58:57,
            59:122, 60:120, 61:99, 62:118, 63:96, 64:97, 65:98, 66:100, 67:101, 68:109, 69:71, 70:107,
            71:89, 72:91, 73:92, 74:78, 75:86, 76:87, 77:88, 78:69, 79:83, 80:84, 81:85, 82:82, 83:65,
            86:10, 87:103, 88:111, 0x11c:76, 0x135:75, 0x138:61, 0x147:115, 0x148:126, 0x149:116,
            0x14b:123, 0x14d:124, 0x14f:119, 0x150:125, 0x151:121, 0x152:114, 0x153:117,
            0x120:74, 0x12e:73, 0x130:72
        ]
        return keys[scan]
    }
}
