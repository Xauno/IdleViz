import Accelerate
import AudioToolbox
import CoreAudio
import Foundation
import os

// SPIKE (step 2, not merged): Core Audio process tap on Spotify only.

/// Keeps the most recent stereo samples. Written on the Core Audio I/O queue, read on the main thread.
final class SampleRing: Sendable {
    static let capacity = 4096
    private struct State {
        var left = [Float](repeating: 0, count: SampleRing.capacity)
        var right = [Float](repeating: 0, count: SampleRing.capacity)
        var head = 0
        var buffers = 0
        var zeroBuffers = 0
    }
    private let state = OSAllocatedUnfairLock(initialState: State())

    func write(_ abl: UnsafePointer<AudioBufferList>, channels: Int) {
        let list = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: abl))
        state.withLockUnchecked { st in
            st.buffers += 1
            var allZero = true
            if list.count >= 2 {
                // Non-interleaved: one buffer per channel.
                guard let l = list[0].mData?.assumingMemoryBound(to: Float.self),
                      let r = list[1].mData?.assumingMemoryBound(to: Float.self) else { return }
                let frames = Int(list[0].mDataByteSize) / MemoryLayout<Float>.size
                for i in 0..<frames {
                    if l[i] != 0 || r[i] != 0 { allZero = false }
                    st.left[st.head] = l[i]
                    st.right[st.head] = r[i]
                    st.head = (st.head + 1) % Self.capacity
                }
            } else if list.count == 1, let p = list[0].mData?.assumingMemoryBound(to: Float.self) {
                let ch = max(Int(list[0].mNumberChannels), 1)
                let frames = Int(list[0].mDataByteSize) / MemoryLayout<Float>.size / ch
                for i in 0..<frames {
                    let l = p[i * ch]
                    let r = ch > 1 ? p[i * ch + 1] : l
                    if l != 0 || r != 0 { allZero = false }
                    st.left[st.head] = l
                    st.right[st.head] = r
                    st.head = (st.head + 1) % Self.capacity
                }
            }
            if allZero { st.zeroBuffers += 1 }
        }
    }

    /// Copies the newest `count` samples per channel, oldest first.
    func latest(_ count: Int, left: inout [Float], right: inout [Float]) {
        state.withLockUnchecked { st in
            var idx = (st.head - count + Self.capacity) % Self.capacity
            for i in 0..<count {
                left[i] = st.left[idx]
                right[i] = st.right[idx]
                idx = (idx + 1) % Self.capacity
            }
        }
    }

    /// Without this, frames built after the tap stops would repeat the last samples forever.
    func clear() {
        state.withLockUnchecked { st in
            st.left = [Float](repeating: 0, count: Self.capacity)
            st.right = [Float](repeating: 0, count: Self.capacity)
        }
    }

    /// Buffers received and how many were exact zeros since the last call.
    func takeCounts() -> (buffers: Int, zero: Int) {
        state.withLockUnchecked { st in
            defer { st.buffers = 0; st.zeroBuffers = 0 }
            return (st.buffers, st.zeroBuffers)
        }
    }
}

enum TapError: Error, CustomStringConvertible {
    case coreAudio(String, OSStatus)
    var description: String {
        switch self {
        case let .coreAudio(what, status): "\(what) failed: \(status)"
        }
    }
}

@MainActor
final class SpotifyAudioTap {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "audio")
    let ring = SampleRing()
    private(set) var sampleRate: Double = 48_000
    private(set) var tappedProcesses: [AudioObjectID] = []
    private var tapID = AudioObjectID(kAudioObjectUnknown)
    private var aggregateID = AudioObjectID(kAudioObjectUnknown)
    private var ioProcID: AudioDeviceIOProcID?
    private let ioQueue = DispatchQueue(label: "com.xauno.IdleViz.audio", qos: .userInteractive)
    private var processListListener: AudioObjectPropertyListenerBlock?
    private var running = false

    func start() {
        guard !running else { return }
        running = true
        rebuild()
        let listener: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
            Task { @MainActor in self?.processListChanged() }
        }
        var addr = Self.address(kAudioHardwarePropertyProcessObjectList)
        AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &addr, .main, listener)
        processListListener = listener
    }

    func stop() {
        guard running else { return }
        running = false
        if let listener = processListListener {
            var addr = Self.address(kAudioHardwarePropertyProcessObjectList)
            AudioObjectRemovePropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &addr, .main, listener)
            processListListener = nil
        }
        teardown()
    }

    private func processListChanged() {
        guard running else { return }
        let now = Self.spotifyProcessObjects()
        guard Set(now) != Set(tappedProcesses) else { return }
        log.notice("Spotify audio processes changed: \(self.tappedProcesses, privacy: .public) → \(now, privacy: .public)")
        rebuild()
    }

    private func rebuild() {
        teardown()
        let processes = Self.spotifyProcessObjects()
        tappedProcesses = processes
        guard !processes.isEmpty else {
            log.notice("No Spotify audio process yet, sending silence")
            return
        }
        do {
            try build(processes)
            log.notice("Tapping \(processes.count) Spotify process(es) at \(self.sampleRate) Hz")
        } catch {
            log.error("Tap setup failed: \(String(describing: error), privacy: .public)")
            teardown()
        }
    }

    private func build(_ processes: [AudioObjectID]) throws {
        let t0 = ContinuousClock.now
        let desc = CATapDescription(stereoMixdownOfProcesses: processes)
        desc.uuid = UUID()
        desc.name = "IdleViz Spotify tap"
        desc.muteBehavior = .unmuted
        desc.isPrivate = true
        var tap = AudioObjectID(kAudioObjectUnknown)
        try check(AudioHardwareCreateProcessTap(desc, &tap), "AudioHardwareCreateProcessTap")
        tapID = tap

        var format = AudioStreamBasicDescription()
        var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        var fmtAddr = Self.address(kAudioTapPropertyFormat)
        try check(AudioObjectGetPropertyData(tap, &fmtAddr, 0, nil, &size, &format), "read tap format")
        sampleRate = format.mSampleRate
        let channels = Int(format.mChannelsPerFrame)
        let interleaved = format.mFormatFlags & kAudioFormatFlagIsNonInterleaved == 0
        log.notice("Tap format: \(format.mSampleRate) Hz, \(channels) ch, interleaved \(interleaved), flags \(format.mFormatFlags)")

        let outputUID = try Self.defaultOutputDeviceUID()
        let description: [String: Any] = [
            kAudioAggregateDeviceNameKey: "IdleViz Spotify Tap",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceMainSubDeviceKey: outputUID,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceIsStackedKey: false,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: outputUID]],
            kAudioAggregateDeviceTapListKey: [
                [kAudioSubTapDriftCompensationKey: true, kAudioSubTapUIDKey: desc.uuid.uuidString]
            ],
        ]
        var aggregate = AudioObjectID(kAudioObjectUnknown)
        try check(AudioHardwareCreateAggregateDevice(description as CFDictionary, &aggregate), "AudioHardwareCreateAggregateDevice")
        aggregateID = aggregate

        var procID: AudioDeviceIOProcID?
        let block = Self.ioBlock(ring: ring, channels: channels)
        try check(AudioDeviceCreateIOProcIDWithBlock(&procID, aggregate, ioQueue, block), "AudioDeviceCreateIOProcIDWithBlock")
        ioProcID = procID
        try check(AudioDeviceStart(aggregate, procID), "AudioDeviceStart")
        log.notice("Tap started in \(String(describing: ContinuousClock.now - t0), privacy: .public)")
    }

    /// Built outside the main actor: a closure formed in a @MainActor method inherits that
    /// isolation, and Swift 6 traps when Core Audio calls it on its I/O thread.
    nonisolated private static func ioBlock(ring: SampleRing, channels: Int) -> AudioDeviceIOBlock {
        { _, input, _, _, _ in ring.write(input, channels: channels) }
    }

    private func teardown() {
        if aggregateID != kAudioObjectUnknown {
            if let procID = ioProcID {
                AudioDeviceStop(aggregateID, procID)
                AudioDeviceDestroyIOProcID(aggregateID, procID)
            }
            AudioHardwareDestroyAggregateDevice(aggregateID)
        }
        if tapID != kAudioObjectUnknown { AudioHardwareDestroyProcessTap(tapID) }
        ring.clear()
        ioProcID = nil
        aggregateID = AudioObjectID(kAudioObjectUnknown)
        tapID = AudioObjectID(kAudioObjectUnknown)
    }

    private func check(_ status: OSStatus, _ what: String) throws {
        if status != noErr { throw TapError.coreAudio(what, status) }
    }

    // MARK: Core Audio lookups

    static func address(_ selector: AudioObjectPropertySelector) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
    }

    static func spotifyProcessObjects() -> [AudioObjectID] {
        let system = AudioObjectID(kAudioObjectSystemObject)
        var addr = address(kAudioHardwarePropertyProcessObjectList)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(system, &addr, 0, nil, &size) == noErr, size > 0 else { return [] }
        var ids = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &addr, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.filter { bundleID(of: $0)?.hasPrefix("com.spotify.client") == true }
    }

    static func bundleID(of object: AudioObjectID) -> String? {
        var addr = address(kAudioProcessPropertyBundleID)
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        var value: Unmanaged<CFString>?
        let status = withUnsafeMutablePointer(to: &value) { AudioObjectGetPropertyData(object, &addr, 0, nil, &size, $0) }
        guard status == noErr, let value else { return nil }
        return value.takeRetainedValue() as String
    }

    static func defaultOutputDeviceUID() throws -> String {
        let system = AudioObjectID(kAudioObjectSystemObject)
        var addr = address(kAudioHardwarePropertyDefaultSystemOutputDevice)
        var device = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        var status = AudioObjectGetPropertyData(system, &addr, 0, nil, &size, &device)
        if status != noErr { throw TapError.coreAudio("default output device", status) }
        var uidAddr = address(kAudioDevicePropertyDeviceUID)
        var uid: Unmanaged<CFString>?
        size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        status = withUnsafeMutablePointer(to: &uid) { AudioObjectGetPropertyData(device, &uidAddr, 0, nil, &size, $0) }
        guard status == noErr, let uid else { throw TapError.coreAudio("output device UID", status) }
        return uid.takeRetainedValue() as String
    }
}

/// Turns the newest 1024 samples into the packed frame the page expects (see web/spike.js).
struct FrameBuilder {
    static let count = 1024
    static let bandCount = 64
    static let frameLength = 24 + bandCount * 4 + count * 4 + 3 * count

    private var left = [Float](repeating: 0, count: count)
    private var right = [Float](repeating: 0, count: count)
    private var mono = [Float](repeating: 0, count: count)
    private var windowed = [Float](repeating: 0, count: count)
    private var magnitudes = [Float](repeating: 0, count: count / 2)
    private var bands = [Float](repeating: 0, count: bandCount)
    private let hann = vDSP.window(ofType: Float.self, usingSequence: .hanningDenormalized, count: count, isHalfWindow: false)
    private let fft = vDSP.FFT(log2n: 10, radix: .radix2, ofType: DSPSplitComplex.self)!
    private var bandEdges: [Int] = []
    private var sequence: UInt32 = 0

    mutating func build(from ring: SampleRing, sampleRate: Double) -> Data {
        ring.latest(Self.count, left: &left, right: &right)
        vDSP.add(left, right, result: &mono)
        vDSP.multiply(0.5, mono, result: &mono)
        let rms = vDSP.rootMeanSquare(mono)
        computeBands(sampleRate: sampleRate)
        let bass = bands[0..<8].reduce(0, +) / 8
        let mid = bands[8..<30].reduce(0, +) / 22
        let treble = bands[30..<64].reduce(0, +) / 34

        var data = Data(capacity: Self.frameLength)
        func append<T>(_ value: T) { withUnsafeBytes(of: value) { data.append(contentsOf: $0) } }
        append(sequence.littleEndian)
        sequence &+= 1
        for v in [Float(sampleRate), bass, mid, treble, rms] { append(v.bitPattern.littleEndian) }
        bands.withUnsafeBytes { data.append(contentsOf: $0) }
        mono.withUnsafeBytes { data.append(contentsOf: $0) }
        for channel in [mono, left, right] {
            data.append(contentsOf: channel.map { UInt8(clamping: Int(($0 * 128).rounded()) + 128) })
        }
        return data
    }

    private mutating func computeBands(sampleRate: Double) {
        vDSP.multiply(mono, hann, result: &windowed)
        var real = [Float](repeating: 0, count: Self.count / 2)
        var imag = [Float](repeating: 0, count: Self.count / 2)
        real.withUnsafeMutableBufferPointer { rp in
            imag.withUnsafeMutableBufferPointer { ip in
                var split = DSPSplitComplex(realp: rp.baseAddress!, imagp: ip.baseAddress!)
                windowed.withUnsafeBytes { raw in
                    vDSP.convert(interleavedComplexVector: Array(raw.bindMemory(to: DSPComplex.self)), toSplitComplexVector: &split)
                }
                fft.forward(input: split, output: &split)
                vDSP.absolute(split, result: &magnitudes)
            }
        }
        if bandEdges.isEmpty { bandEdges = Self.logEdges(sampleRate: sampleRate) }
        // Crude noise floor + scaling, enough to see whether levels look alive.
        for b in 0..<Self.bandCount {
            let lo = bandEdges[b], hi = max(bandEdges[b + 1], lo + 1)
            let avg = magnitudes[lo..<hi].reduce(0, +) / Float(hi - lo)
            let db = 20 * log10(max(avg / Float(Self.count), 1e-9))
            let level = min(max((db + 70) / 60, 0), 1)
            bands[b] = level > bands[b] ? level : bands[b] * 0.9 + level * 0.1
        }
    }

    private static func logEdges(sampleRate: Double) -> [Int] {
        let binHz = sampleRate / Double(count)
        return (0...bandCount).map { i in
            let hz = 40 * pow(16_000.0 / 40, Double(i) / Double(bandCount))
            return min(max(Int(hz / binHz), 1), count / 2 - 1)
        }
    }
}
