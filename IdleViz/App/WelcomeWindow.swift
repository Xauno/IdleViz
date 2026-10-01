import AppKit
import IdleVizCore
import SwiftUI

/// The app is an accessory (no Dock icon) except while one of its normal windows is open, when
/// it's a regular app so the window can take focus and ⌘Q works.
@MainActor
enum RegularWindows {
    private static var open: Set<ObjectIdentifier> = []

    static func show(_ window: NSWindow) {
        open.insert(ObjectIdentifier(window))
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
    }

    static func closed(_ window: NSWindow) {
        open.remove(ObjectIdentifier(window))
        if open.isEmpty { NSApp.setActivationPolicy(.accessory) }
    }
}

/// Explains the two required permissions and shows their macOS prompts on purpose, while someone
/// is at the Mac, so a prompt never comes up during an idle open. It appears at launch whenever
/// macOS hasn't asked about one of them yet, and from the popup's error rows.
@MainActor
final class WelcomeWindowController: NSObject, NSWindowDelegate {
    private let permissions: Permissions
    private var window: NSWindow?

    init(permissions: Permissions) {
        self.permissions = permissions
    }

    func show() {
        let window = window ?? makeWindow()
        self.window = window
        RegularWindows.show(window)
    }

    func windowWillClose(_ notification: Notification) {
        permissions.cancelRequest()
        if let window { RegularWindows.closed(window) }
    }

    private func makeWindow() -> NSWindow {
        let view = WelcomeView(permissions: permissions) { [weak self] in self?.window?.close() }
        let hosting = NSHostingController(rootView: view)
        let window = NSWindow(contentViewController: hosting)
        window.styleMask = [.titled, .closable]
        window.title = "IdleViz"
        window.collectionBehavior = [.fullScreenNone]
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
        return window
    }
}

struct WelcomeView: View {
    var permissions: Permissions
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Welcome to IdleViz").font(.title2).fontWeight(.semibold)
                Text(intro).foregroundStyle(.secondary)
            }
            VStack(spacing: 0) {
                ForEach(RequiredPermission.allCases) { permission in
                    if permission != RequiredPermission.allCases.first { Divider() }
                    row(permission)
                }
            }
            .padding(.horizontal, 12)
            .background(.quaternary.opacity(0.5), in: .rect(cornerRadius: 10))
            HStack {
                if permissions.waitingForSpotify {
                    Text("Open Spotify. This continues by itself once it's running.")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                if permissions.status.needsWelcome {
                    Button("Continue") { permissions.requestMissing() }
                        .keyboardShortcut(.defaultAction)
                        .disabled(permissions.isRequesting)
                } else {
                    Button("Done", action: close).keyboardShortcut(.defaultAction)
                }
            }
        }
        .padding(20)
        .frame(width: 420)
        .fixedSize(horizontal: false, vertical: true)
    }

    private var intro: String {
        if permissions.status.needsWelcome {
            "IdleViz needs two permissions from macOS. Continue asks for each in turn."
        } else if permissions.status.missing.isEmpty {
            "IdleViz has the two permissions it needs from macOS."
        } else {
            "IdleViz needs two permissions from macOS. Switch on the missing one in System Settings."
        }
    }

    private func row(_ permission: RequiredPermission) -> some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 2) {
                Text(permission.name)
                Text(permission.explanation).font(.callout).foregroundStyle(.secondary)
            }
            Spacer()
            state(permission)
        }
        .padding(.vertical, 10)
    }

    @ViewBuilder private func state(_ permission: RequiredPermission) -> some View {
        switch permissions.status[permission] {
        case .granted:
            Label("Allowed", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
        case .denied:
            VStack(alignment: .trailing, spacing: 2) {
                Text("Not allowed").foregroundStyle(.orange)
                // macOS asks only once. After a no, the switch in System Settings is the only way.
                Link("Open System Settings…", destination: permission.settingsURL).font(.callout)
            }
        case .notAsked:
            Text(permissions.asking == permission ? "Asking…" : "Not asked").foregroundStyle(.secondary)
        case .unknown:
            // Automation can only be checked while Spotify runs. Audio only lands here if macOS can't be asked.
            Text(permission == .automation ? "Needs Spotify running" : "Asked on first use").foregroundStyle(.secondary)
        }
    }
}
