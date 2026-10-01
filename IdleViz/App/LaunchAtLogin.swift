import os
import ServiceManagement
import SwiftUI

/// The "Launch at login" row in settings. macOS keeps the state (System Settings → General →
/// Login Items), so the switch reads it from there instead of storing a setting of its own.
struct LaunchAtLoginToggle: View {
    @State private var status = SMAppService.mainApp.status
    @State private var failed = false
    private let log = Logger(subsystem: "com.xauno.IdleViz", category: "app")

    var body: some View {
        Toggle(isOn: Binding(get: { status == .enabled }, set: set)) {
            Text("Launch at login")
            if failed {
                Text("Couldn't change it. Try from the copy in Applications.")
            } else if status == .requiresApproval {
                Text("Allow IdleViz under Login Items in System Settings")
            }
        }
        // It can be switched off in System Settings while this window is open.
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            status = SMAppService.mainApp.status
        }
    }

    private func set(_ enabled: Bool) {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            failed = false
        } catch {
            failed = true
            log.error("Launch at login: \(error.localizedDescription, privacy: .public)")
        }
        status = SMAppService.mainApp.status
    }
}
