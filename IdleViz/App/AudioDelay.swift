import AVFoundation
import CoreAudio
import IdleVizCore
import Observation
import os

/// The audio delay for the current output device: loads and saves it per device, follows the
/// default output device, and runs Detect delay.
@MainActor
@Observable
final class AudioDelayController {
    enum Hint: Equatable {
        case text(String)
        /// Microphone access was denied; the row links to System Settings.
        case microphoneDenied
    }

    static let listeningSeconds = 5.0
    static let microphoneSettingsURL = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone")!

    /// Seconds, on the slider's 10 ms steps. Setting it saves it for the current device.
    var delay: Double = 0 {
        didSet {
            let normalized = AudioDelaySetting.normalized(delay)
            guard normalized == delay else {
                delay = normalized
                return
            }
            guard delay != oldValue else { return }
            if !loading { save() }
            onChange?(delay)
        }
    }
    /// The speakers or headphones the delay belongs to.
    private(set) var deviceName = "this device"
    private(set) var detecting = false
    private(set) var hint: Hint?

    /// Called with the new delay whenever it changes, by hand, by Detect delay or with the device.
    @ObservationIgnored var onChange: ((Double) -> Void)?
    /// Whether Spotify says it's playing; Detect delay needs music.
    @ObservationIgnored var spotifyIsPlaying: () -> Bool = { false }

    @ObservationIgnored private let log = Logger(subsystem: "com.xauno.IdleViz", category: "delay")
    @ObservationIgnored private let pump: AudioPump
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private var deviceUID = ""
    @ObservationIgnored private var loading = false
    @ObservationIgnored private var listener: AudioObjectPropertyListenerBlock?

    init(pump: AudioPump, defaults: UserDefaults = .standard) {
        self.pump = pump
        self.defaults = defaults
        loadDevice()
        let listener: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
            Task { @MainActor in self?.loadDevice() }
        }
        var address = SpotifyAudioTap.address(kAudioHardwarePropertyDefaultOutputDevice)
        AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, .main, listener)
        self.listener = listener
    }

    /// Switches to the default output device's delay: its saved one, or what macOS reports for it.
    private func loadDevice() {
        guard let device = AudioDevices.defaultOutput() else { return }
        let uid = AudioDevices.uid(of: device) ?? ""
        deviceUID = uid
        deviceName = AudioDevices.name(of: device) ?? "this device"
        let reported = AudioDevices.latency(of: device, scope: kAudioObjectPropertyScopeOutput)
        let saved = AudioDelaySetting.saved(in: defaults)
        hint = nil
        loading = true
        delay = AudioDelaySetting.delay(forDevice: uid, saved: saved, reportedLatency: reported)
        loading = false
        let source = saved[uid] == nil ? "reported by macOS" : "saved"
        log.notice("""
            Output device: \(self.deviceName, privacy: .public), delay \(AudioDelaySetting.label(self.delay), privacy: .public) \
            (\(source, privacy: .public))
            """)
        // Tell the listeners even if the number is the same as the last device's.
        onChange?(delay)
    }

    private func save() {
        guard !deviceUID.isEmpty else { return }
        var saved = AudioDelaySetting.saved(in: defaults)
        saved[deviceUID] = delay
        defaults.set(saved, forKey: AudioDelaySetting.key)
    }

    // MARK: Detect delay

    /// Listens with the microphone for a few seconds while Spotify plays, and sets the delay
    /// from how far the sound lags behind the audio Spotify sent.
    func detect() {
        guard !detecting else { return }
        guard spotifyIsPlaying() else {
            hint = .text("Play something in Spotify first, out loud")
            return
        }
        detecting = true
        hint = nil
        Task {
            defer { detecting = false }
            guard await microphoneAllowed() else {
                hint = .microphoneDenied
                return
            }
            await measure()
        }
    }

    private func microphoneAllowed() async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized: true
        case .notDetermined: await AVCaptureDevice.requestAccess(for: .audio)
        default: false
        }
    }

    private func measure() async {
        let microphone = MicrophoneRecorder()
        pump.startRecording(seconds: Self.listeningSeconds + 1)
        do {
            try microphone.start()
        } catch {
            _ = pump.stopRecording()
            log.error("Couldn't start the microphone: \(error.localizedDescription, privacy: .public)")
            hint = .text("Couldn't use the microphone")
            return
        }
        try? await Task.sleep(for: .seconds(Self.listeningSeconds))
        // The microphone stops right here. Its audio is only held in memory for the math below.
        let heard = microphone.stop()
        let sent = pump.stopRecording()
        let inputLatency = microphone.inputLatency
        let peak = await Task.detached(priority: .userInitiated) {
            DelayDetector.peak(sent: sent, heard: heard, inputLatency: inputLatency)
        }.value
        logDiagnostics(sent: sent, heard: heard, inputLatency: inputLatency, peak: peak)
        let result = DelayDetector.delay(from: peak)

        if let result {
            log.notice("Detected a delay of \(AudioDelaySetting.label(result), privacy: .public) for \(self.deviceName, privacy: .public)")
            let unchanged = result == delay
            delay = result
            // Save it even when it matches the reported latency, so this device counts as measured.
            if unchanged { save() }
            hint = .text("Measured \(AudioDelaySetting.label(result))")
        } else {
            log.notice("""
                Couldn't detect the delay (\(sent.samples.count, privacy: .public) tap samples, \
                \(heard.samples.count, privacy: .public) microphone samples)
                """)
            hint = .text("Couldn't hear the music clearly (too quiet, or headphones). Kept \(AudioDelaySetting.label(delay)).")
        }
    }

    /// Only visible with `log stream --level debug`: what the two recordings looked like and the
    /// best lag between them, whether or not it was clear enough to use.
    private func logDiagnostics(
        sent: DelayDetector.Recording, heard: DelayDetector.Recording, inputLatency: Double, peak: DelayDetector.Peak?
    ) {
        func level(_ samples: [Float]) -> Float {
            guard !samples.isEmpty else { return -.infinity }
            return 10 * log10(samples.reduce(0) { $0 + $1 * $1 } / Float(samples.count) + 1e-12)
        }
        let now = AVAudioTime.seconds(forHostTime: mach_absolute_time())
        log.debug("""
            Tap: \(sent.samples.count, privacy: .public) samples at \(sent.sampleRate, privacy: .public) Hz, \
            \(level(sent.samples), privacy: .public) dB, started \(now - sent.start, privacy: .public) s ago. \
            Microphone: \(heard.samples.count, privacy: .public) samples at \(heard.sampleRate, privacy: .public) Hz, \
            \(level(heard.samples), privacy: .public) dB, started \(now - heard.start, privacy: .public) s ago, \
            latency \(inputLatency, privacy: .public) s. \
            Peak: lag \(peak?.delay ?? -1, privacy: .public) s, z-score \(peak?.zScore ?? 0, privacy: .public), \
            ratio \(peak?.ratio ?? 0, privacy: .public)
            """)
    }
}

/// Records the microphone for Detect delay. The audio never leaves this object except as the
/// recording handed to the delay math, and nothing is written to disk.
final class MicrophoneRecorder: @unchecked Sendable {
    private struct State {
        var samples: [Float] = []
        var startHostTime: UInt64 = 0
    }

    private let engine = AVAudioEngine()
    private let state = OSAllocatedUnfairLock(initialState: State())
    private var sampleRate = 48_000.0
    /// The microphone's own latency in seconds: sound reaches the recording this much late.
    private(set) var inputLatency = 0.0

    func start() throws {
        let input = engine.inputNode
        // Prefer the built-in microphone. Recording from a Bluetooth headset's microphone would
        // switch the headset to its call mode and change the very delay being measured.
        if let builtIn = AudioDevices.builtInMicrophone() {
            try input.auAudioUnit.setDeviceID(builtIn)
            inputLatency = AudioDevices.latency(of: builtIn, scope: kAudioObjectPropertyScopeInput)
        } else if let device = AudioDevices.defaultInput() {
            inputLatency = AudioDevices.latency(of: device, scope: kAudioObjectPropertyScopeInput)
        }
        // The microphone's own format. The node's output format follows the output device's sample
        // rate instead, and a tap in that format hears nothing when the two rates differ (AirPlay).
        let format = input.inputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0 else {
            throw NSError(domain: "IdleViz", code: 1, userInfo: [NSLocalizedDescriptionKey: "No microphone found"])
        }
        sampleRate = format.sampleRate
        let state = state
        input.installTap(onBus: 0, bufferSize: 4096, format: format) { buffer, time in
            guard let channel = buffer.floatChannelData?[0] else { return }
            let frames = Int(buffer.frameLength)
            state.withLockUnchecked { state in
                if state.samples.isEmpty, time.isHostTimeValid { state.startHostTime = time.hostTime }
                state.samples.append(contentsOf: UnsafeBufferPointer(start: channel, count: frames))
            }
        }
        engine.prepare()
        try engine.start()
    }

    /// Stops the microphone and returns what it heard.
    func stop() -> DelayDetector.Recording {
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        let (samples, startHostTime) = state.withLockUnchecked { ($0.samples, $0.startHostTime) }
        return DelayDetector.Recording(samples: samples, sampleRate: sampleRate, start: AVAudioTime.seconds(forHostTime: startHostTime))
    }
}

/// Core Audio lookups for the delay: devices, their names and their latency.
enum AudioDevices {
    static func defaultOutput() -> AudioObjectID? { defaultDevice(kAudioHardwarePropertyDefaultOutputDevice) }
    static func defaultInput() -> AudioObjectID? { defaultDevice(kAudioHardwarePropertyDefaultInputDevice) }

    private static func defaultDevice(_ selector: AudioObjectPropertySelector) -> AudioObjectID? {
        var address = SpotifyAudioTap.address(selector)
        var device = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        let status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &device)
        return status == noErr && device != kAudioObjectUnknown ? device : nil
    }

    static func uid(of device: AudioObjectID) -> String? {
        SpotifyAudioTap.string(kAudioDevicePropertyDeviceUID, of: device)
    }

    static func name(of device: AudioObjectID) -> String? {
        SpotifyAudioTap.string(kAudioObjectPropertyName, of: device)
    }

    /// The device's latency in seconds as macOS reports it: the device's own latency, its safety
    /// offset and its first stream's latency, at the device's sample rate.
    static func latency(of device: AudioObjectID, scope: AudioObjectPropertyScope) -> Double {
        var frames = number(kAudioDevicePropertyLatency, of: device, scope: scope)
        frames += number(kAudioDevicePropertySafetyOffset, of: device, scope: scope)
        if let stream = streams(of: device, scope: scope).first {
            frames += number(kAudioStreamPropertyLatency, of: stream, scope: kAudioObjectPropertyScopeGlobal)
        }
        var rate = Float64(0)
        var size = UInt32(MemoryLayout<Float64>.size)
        var address = SpotifyAudioTap.address(kAudioDevicePropertyNominalSampleRate)
        guard AudioObjectGetPropertyData(device, &address, 0, nil, &size, &rate) == noErr, rate > 0 else { return 0 }
        return Double(frames) / rate
    }

    /// The Mac's own microphone, if it has one.
    static func builtInMicrophone() -> AudioObjectID? {
        var address = SpotifyAudioTap.address(kAudioHardwarePropertyDevices)
        var size: UInt32 = 0
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard AudioObjectGetPropertyDataSize(system, &address, 0, nil, &size) == noErr, size > 0 else { return nil }
        var devices = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &address, 0, nil, &size, &devices) == noErr else { return nil }
        return devices.first { device in
            let transport = number(kAudioDevicePropertyTransportType, of: device, scope: kAudioObjectPropertyScopeGlobal)
            return transport == kAudioDeviceTransportTypeBuiltIn && !streams(of: device, scope: kAudioObjectPropertyScopeInput).isEmpty
        }
    }

    private static func number(
        _ selector: AudioObjectPropertySelector, of object: AudioObjectID, scope: AudioObjectPropertyScope
    ) -> UInt32 {
        var address = AudioObjectPropertyAddress(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
        var value: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        return AudioObjectGetPropertyData(object, &address, 0, nil, &size, &value) == noErr ? value : 0
    }

    private static func streams(of device: AudioObjectID, scope: AudioObjectPropertyScope) -> [AudioObjectID] {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreams, mScope: scope, mElement: kAudioObjectPropertyElementMain
        )
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(device, &address, 0, nil, &size) == noErr, size > 0 else { return [] }
        var streams = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        return AudioObjectGetPropertyData(device, &address, 0, nil, &size, &streams) == noErr ? streams : []
    }
}
