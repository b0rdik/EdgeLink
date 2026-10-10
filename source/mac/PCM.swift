import Foundation

enum AudioPCM {
    // 240 stereo frames of signed 16-bit PCM (5 ms at 48 kHz).
    static let packetBytes = 960

    static func takePacket(from buffer: inout Data) -> Data? {
        guard buffer.count >= packetBytes else { return nil }
        // Data.removeFirst can retain a nonzero startIndex. Count is a length,
        // so packet ranges must be relative to the collection's actual start.
        let end = buffer.index(buffer.startIndex, offsetBy: packetBytes)
        let packet = buffer.subdata(in: buffer.startIndex..<end)
        buffer.removeFirst(packetBytes)
        return packet
    }
}
