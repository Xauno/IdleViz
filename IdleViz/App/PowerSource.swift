import Foundation
import IdleVizCore
import IOKit.ps

/// Whether the Mac runs on battery. macOS reports changes, so nothing polls.
@MainActor
final class PowerSource {
    /// False on a Mac mini, iMac, Mac Studio or Mac Pro, where the battery settings are hidden.
    static let hasBattery: Bool = {
        let info = IOPSCopyPowerSourcesInfo().takeRetainedValue()
        let sources = IOPSCopyPowerSourcesList(info).takeRetainedValue() as [CFTypeRef]
        return sources.contains { source in
            let description = IOPSGetPowerSourceDescription(info, source)?.takeUnretainedValue() as? [String: Any]
            return description?[kIOPSTypeKey] as? String == kIOPSInternalBatteryType
        }
    }()

    private(set) var onBattery = PowerSource.read()
    /// Called when the Mac is plugged in or unplugged.
    var onChange: (() -> Void)?

    init() {
        // The app delegate keeps this object for as long as the app runs, so the pointer stays valid.
        let context = Unmanaged.passUnretained(self).toOpaque()
        let source = IOPSNotificationCreateRunLoopSource({ context in
            guard let context else { return }
            let power = Unmanaged<PowerSource>.fromOpaque(context).takeUnretainedValue()
            MainActor.assumeIsolated { power.refresh() }
        }, context)
        if let source { CFRunLoopAddSource(CFRunLoopGetMain(), source.takeRetainedValue(), .commonModes) }
    }

    /// The notification also fires when only the charge level changed.
    private func refresh() {
        let now = Self.read()
        guard now != onBattery else { return }
        onBattery = now
        onChange?()
    }

    private static func read() -> Bool {
        guard let type = IOPSGetProvidingPowerSourceType(nil)?.takeUnretainedValue() else { return false }
        return PowerSourceType.isBattery(type as String)
    }
}
