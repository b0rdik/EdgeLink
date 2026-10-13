import Foundation
import ScreenCaptureKit
import CoreMedia
import AVFoundation
import Darwin
import OSLog

final class AudioSender: NSObject, SCStreamOutput, SCStreamDelegate {
    private(set) var isCapturing = false
    private var stream: SCStream?
    private let queue = DispatchQueue(label: "EdgeLink.audio", qos: .userInteractive)
    private let server: LinkServer
    private var udp: Int32 = -1
    private var accumulated = Data()
    private var sequence: UInt64 = 0, firstFrame: UInt64 = 0
    private weak var previous: Session?
    private var lastPTS: Double?
    private let metrics = NSLock()
    private var batchFrames = 0
    private let logger = Logger(subsystem: "local.edgelink.mac", category: "Audio")
    private var reportAfter = ProcessInfo.processInfo.systemUptime + 5
    private var peak: Float = 0
    var captureBatchFrames: Int { metrics.lock(); defer { metrics.unlock() }; return batchFrames }
    var failed: ((String) -> Void)?
    init(server: LinkServer) { self.server = server }
    func start(ip: String, displayID: CGDirectDisplayID) async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let display = content.displays.first(where: { $0.displayID == displayID }) else { throw LinkError.message("Экран Mac не найден") }
        let fd = socket(AF_INET, SOCK_DGRAM, 0)
        guard fd >= 0 else { throw LinkError.message("Audio socket error") }
        do { try Wire.bind(fd, ip: ip, port: 0) } catch { close(fd); throw error }
        udp = fd
        let configuration = SCStreamConfiguration()
        configuration.width = 2; configuration.height = 2
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 1)
        configuration.capturesAudio = true; configuration.sampleRate = 48000; configuration.channelCount = 2
        configuration.excludesCurrentProcessAudio = true; configuration.captureMicrophone = false
        let filter = SCContentFilter(display: display, excludingWindows: [])
        let s = SCStream(filter: filter, configuration: configuration, delegate: self)
        try s.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
        stream = s
        do { try await s.startCapture(); isCapturing = true } catch { isCapturing = false; stream = nil; close(fd); udp = -1; throw error }
    }
    func stop() async {
        let s = stream; stream = nil; isCapturing = false
        try? await s?.stopCapture()
        queue.sync { if udp >= 0 { close(udp); udp = -1 }; accumulated.removeAll(); lastPTS = nil }
    }
    func stream(_ stream: SCStream, didStopWithError error: Error) {
        DispatchQueue.main.async { [weak self] in
            guard let self, self.stream === stream else { return }
            self.isCapturing = false; self.failed?("Захват звука остановлен: \(error.localizedDescription)")
        }
    }
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        if type == .audio, CMSampleBufferDataIsReady(sampleBuffer) {
            metrics.lock(); batchFrames = max(batchFrames, CMSampleBufferGetNumSamples(sampleBuffer)); metrics.unlock()
        }
        guard type == .audio, CMSampleBufferDataIsReady(sampleBuffer), udp >= 0, let session = server.currentSession else {
            accumulated.removeAll(keepingCapacity: true); lastPTS = nil; return
        }
        if previous !== session { accumulated.removeAll(keepingCapacity: true); previous = session; firstFrame = 0; sequence = 0; lastPTS = nil }
        guard let format = CMSampleBufferGetFormatDescription(sampleBuffer),
              let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(format) else { return }
        guard asbd.pointee.mSampleRate == 48000, asbd.pointee.mBitsPerChannel == 32,
              asbd.pointee.mFormatFlags & kAudioFormatFlagIsFloat != 0 else { return }
        let frames = CMSampleBufferGetNumSamples(sampleBuffer)
        let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer).seconds
        // Preserve capture discontinuities rather than concatenating old and new audio.
        if let expected = lastPTS, pts.isFinite, abs(pts - expected) > 0.015 {
            accumulated.removeAll(keepingCapacity: true); firstFrame += 4800
        }
        lastPTS = pts + Double(frames) / 48000
        let size = MemoryLayout<AudioBufferList>.size + MemoryLayout<AudioBuffer>.size
        let memory = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: 16)
        defer { memory.deallocate() }
        let list = memory.bindMemory(to: AudioBufferList.self, capacity: 1)
        var block: CMBlockBuffer?
        guard CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sampleBuffer, bufferListSizeNeededOut: nil,
            bufferListOut: list, bufferListSize: size, blockBufferAllocator: kCFAllocatorDefault,
            blockBufferMemoryAllocator: kCFAllocatorDefault, flags: UInt32(kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment), blockBufferOut: &block) == noErr else { return }
        let buffers = UnsafeMutableAudioBufferListPointer(list)
        guard !buffers.isEmpty, let leftRaw = buffers[0].mData else { return }
        let left = leftRaw.assumingMemoryBound(to: Float.self)
        let planar = asbd.pointee.mFormatFlags & kAudioFormatFlagIsNonInterleaved != 0
        let channels = Int(asbd.pointee.mChannelsPerFrame)
        let right = planar && buffers.count > 1 ? buffers[1].mData?.assumingMemoryBound(to: Float.self) : nil
        for frame in 0..<frames {
            let l = left[planar ? frame : frame * channels]
            let r = planar ? (right?[frame] ?? l) : left[frame * channels + min(1, channels - 1)]
            if l.isFinite && r.isFinite { peak = max(peak, abs(l), abs(r)) }
            accumulated.appendLE(pcm(l)); accumulated.appendLE(pcm(r))
        }
        while let pcm = AudioPCM.takePacket(from: &accumulated) {
            sequence += 1
            var plain = Data([2]); plain.appendLE(sequence); plain.appendLE(firstFrame)
            plain.append(pcm)
            if let packet = try? Wire.seal(plain, key: session.key) { _ = Wire.sendUDP(udp, data: packet, to: session.audioTarget) }
            firstFrame += 240
        }
        let now = ProcessInfo.processInfo.systemUptime
        if now >= reportAfter {
            logger.info("Audio capture batch=\(frames, privacy: .public) peak=\(self.peak, privacy: .public)")
            peak = 0; reportAfter = now + 5
        }
    }
    private func pcm(_ value: Float) -> Int16 {
        guard value.isFinite else { return 0 }
        return Int16(max(-32768, min(32767, Int(max(-1, min(1, value)) * 32768))))
    }
}
