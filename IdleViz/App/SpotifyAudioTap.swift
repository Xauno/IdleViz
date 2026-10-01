import AudioToolbox
import CoreAudio
import Foundation
import IdleVizCore
import os

enum TapError: Error, CustomStringConvertible {
    case coreAudio(String, OSStatus)

    var description: String {
        switch self {
        case let .coreAudio(what, status): "\(what) failed: \(status)"
        }
    }
}

/// A Core Audio process tap on Spotify, and only Spotify. It is the one audio source the
/// visuals ever get: never a system-wide tap, an input device or the microphone. When Spotify
/// has no audio process, nothing is tapped and the ring holds silence.
@MainActor
final class SpotifyAudioTap {
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "audio")
    let ring = SampleRing()
    private(set) var sampleRate: Double = 48_000
    private var tappedProcesses: [AudioObjectID] = []
    private var tapID = AudioObjectID(kAudioObjectUnknown)
    private var aggregateID = AudioObjectID(kAudioObjectUnknown)
    private var ioProcID: AudioDeviceIOProcID?
    private let ioQueue = DispatchQueue(label: "com.xauno.IdleViz.audio", qos: .userInteractive)
    private var listeners: [(AudioObjectPropertySelector, AudioObjectPropertyListenerBlock)] = []
    private var running = false
    /// Who needs the tap right now: the open window, Detect delay, or both.
    private var users = 0

    /// Starts the tap for one more user. Balance with `release()`.
    func retain() {
        users += 1
        if users == 1 { start() }
    }

    func release() {
        users = max(users - 1, 0)
        if users == 0 { stop() }
    }

    /// True while Spotify has an audio process and the tap on it is running.
    var isTapping: Bool { ioProcID != nil }

    private func start() {
        guard !running else { return }
        running = true
        rebuild()
        // Spotify's audio process comes and goes with the app, and the tap's clock is the output
        // device, so both changes mean building the tap again.
        for selector in [kAudioHardwarePropertyProcessObjectList, kAudioHardwarePropertyDefaultOutputDevice] {
            let listener: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
                Task { @MainActor in self?.hardwareChanged(selector) }
            }
            var address = Self.address(selector)
            AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, .main, listener)
            listeners.append((selector, listener))
        }
    }

    private func stop() {
        guard running else { return }
        running = false
        for (selector, listener) in listeners {
            var address = Self.address(selector)
            AudioObjectRemovePropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, .main, listener)
        }
        listeners = []
        teardown()
    }

    private func hardwareChanged(_ selector: AudioObjectPropertySelector) {
        guard running else { return }
        if selector == kAudioHardwarePropertyProcessObjectList {
            let now = Self.spotifyProcessObjects()
            guard Set(now) != Set(tappedProcesses) else { return }
            log.notice("Spotify audio processes changed: \(self.tappedProcesses, privacy: .public) → \(now, privacy: .public)")
        } else {
            log.notice("Default output device changed")
        }
        rebuild()
    }

    private func rebuild() {
        teardown()
        let processes = Self.spotifyProcessObjects()
        tappedProcesses = processes
        guard !processes.isEmpty else {
            log.notice("No Spotify audio process, sending silence")
            return
        }
        do {
            try build(processes)
            log.notice("Tapping \(processes.count, privacy: .public) Spotify process(es) at \(self.sampleRate, privacy: .public) Hz")
        } catch {
            log.error("Tap setup failed: \(String(describing: error), privacy: .public)")
            teardown()
        }
    }

    private func build(_ processes: [AudioObjectID]) throws {
        let description = CATapDescription(stereoMixdownOfProcesses: processes)
        description.uuid = UUID()
        description.name = "IdleViz Spotify tap"
        description.muteBehavior = .unmuted
        description.isPrivate = true
        var tap = AudioObjectID(kAudioObjectUnknown)
        try check(AudioHardwareCreateProcessTap(description, &tap), "AudioHardwareCreateProcessTap")
        tapID = tap

        var format = AudioStreamBasicDescription()
        var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        var formatAddress = Self.address(kAudioTapPropertyFormat)
        try check(AudioObjectGetPropertyData(tap, &formatAddress, 0, nil, &size, &format), "reading the tap format")
        if format.mSampleRate > 0 { sampleRate = format.mSampleRate }

        let outputUID = try Self.defaultOutputDeviceUID()
        let aggregateDescription: [String: Any] = [
            kAudioAggregateDeviceNameKey: "IdleViz Spotify Tap",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceMainSubDeviceKey: outputUID,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceIsStackedKey: false,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: outputUID]],
            kAudioAggregateDeviceTapListKey: [
                [kAudioSubTapDriftCompensationKey: true, kAudioSubTapUIDKey: description.uuid.uuidString]
            ],
        ]
        var aggregate = AudioObjectID(kAudioObjectUnknown)
        try check(
            AudioHardwareCreateAggregateDevice(aggregateDescription as CFDictionary, &aggregate),
            "AudioHardwareCreateAggregateDevice"
        )
        aggregateID = aggregate
        // The tap's own format can name another rate than it delivers: the samples arrive at the
        // aggregate's rate, which is the output device's (44,100 Hz on AirPlay, for example).
        var rate = Float64(0)
        var rateSize = UInt32(MemoryLayout<Float64>.size)
        var rateAddress = Self.address(kAudioDevicePropertyNominalSampleRate)
        if AudioObjectGetPropertyData(aggregate, &rateAddress, 0, nil, &rateSize, &rate) == noErr, rate > 0 { sampleRate = rate }

        var procID: AudioDeviceIOProcID?
        let block = Self.ioBlock(ring: ring)
        try check(AudioDeviceCreateIOProcIDWithBlock(&procID, aggregate, ioQueue, block), "AudioDeviceCreateIOProcIDWithBlock")
        ioProcID = procID
        try check(AudioDeviceStart(aggregate, procID), "AudioDeviceStart")
    }

    /// Built outside the main actor: a closure formed in a `@MainActor` method inherits that
    /// isolation, and Swift 6 traps when Core Audio calls it on its I/O thread.
    private nonisolated static func ioBlock(ring: SampleRing) -> AudioDeviceIOBlock {
        { now, input, _, _, _ in
            // When the buffer is handed over, not when its samples are stamped: the visuals are
            // delayed from the moment they get the audio, so Detect delay measures from there too.
            let hostTime = now.pointee.mFlags.contains(.hostTimeValid) ? now.pointee.mHostTime : mach_absolute_time()
            let buffers = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: input))
            if buffers.count >= 2 {
                // One buffer per channel.
                guard let left = buffers[0].mData?.assumingMemoryBound(to: Float.self),
                      let right = buffers[1].mData?.assumingMemoryBound(to: Float.self) else { return }
                let frames = Int(min(buffers[0].mDataByteSize, buffers[1].mDataByteSize)) / MemoryLayout<Float>.size
                ring.append(left: left, right: right, frames: frames, hostTime: hostTime)
            } else if buffers.count == 1, let samples = buffers[0].mData?.assumingMemoryBound(to: Float.self) {
                let channels = max(Int(buffers[0].mNumberChannels), 1)
                let frames = Int(buffers[0].mDataByteSize) / MemoryLayout<Float>.size / channels
                ring.append(interleaved: samples, channels: channels, frames: frames, hostTime: hostTime)
            }
        }
    }

    private func teardown() {
        if aggregateID != kAudioObjectUnknown {
            if let ioProcID {
                AudioDeviceStop(aggregateID, ioProcID)
                AudioDeviceDestroyIOProcID(aggregateID, ioProcID)
            }
            AudioHardwareDestroyAggregateDevice(aggregateID)
        }
        if tapID != kAudioObjectUnknown { AudioHardwareDestroyProcessTap(tapID) }
        // Frames built from here on must be silence, not the last samples repeated.
        ring.clear()
        ioProcID = nil
        aggregateID = AudioObjectID(kAudioObjectUnknown)
        tapID = AudioObjectID(kAudioObjectUnknown)
    }

    private func check(_ status: OSStatus, _ what: String) throws {
        if status != noErr { throw TapError.coreAudio(what, status) }
    }

    // MARK: Core Audio lookups

    nonisolated static func address(_ selector: AudioObjectPropertySelector) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
    }

    /// Every audio process object that belongs to Spotify, helper processes included.
    static func spotifyProcessObjects() -> [AudioObjectID] {
        let system = AudioObjectID(kAudioObjectSystemObject)
        var address = address(kAudioHardwarePropertyProcessObjectList)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(system, &address, 0, nil, &size) == noErr, size > 0 else { return [] }
        var ids = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &address, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.filter { bundleID(of: $0)?.hasPrefix(SpotifyInfo.bundleID) == true }
    }

    private static func bundleID(of object: AudioObjectID) -> String? {
        string(kAudioProcessPropertyBundleID, of: object)
    }

    static func defaultOutputDeviceUID() throws -> String {
        let system = AudioObjectID(kAudioObjectSystemObject)
        var address = address(kAudioHardwarePropertyDefaultOutputDevice)
        var device = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        let status = AudioObjectGetPropertyData(system, &address, 0, nil, &size, &device)
        if status != noErr { throw TapError.coreAudio("reading the default output device", status) }
        guard let uid = string(kAudioDevicePropertyDeviceUID, of: device) else {
            throw TapError.coreAudio("reading the output device UID", kAudioHardwareUnknownPropertyError)
        }
        return uid
    }

    nonisolated static func string(_ selector: AudioObjectPropertySelector, of object: AudioObjectID) -> String? {
        var address = address(selector)
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        var value: Unmanaged<CFString>?
        let status = withUnsafeMutablePointer(to: &value) { AudioObjectGetPropertyData(object, &address, 0, nil, &size, $0) }
        guard status == noErr, let value else { return nil }
        return value.takeRetainedValue() as String
    }
}
