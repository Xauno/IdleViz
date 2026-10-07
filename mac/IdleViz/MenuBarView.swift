import IdleVizCore
import KeyboardShortcuts
import SwiftUI

/// The small glass popup under the menu-bar icon.
struct MenuBarView: View {
    var permissions: Permissions
    let openSettings: () -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var shortcut = KeyboardShortcuts.getShortcut(for: .openVisualizer)

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Button {
                dismiss()
                openSettings()
            } label: {
                HStack {
                    Text("Settings…")
                    Spacer()
                    Text("⌘,").foregroundStyle(.secondary)
                }
            }
            .buttonStyle(MenuRowButtonStyle())
            .keyboardShortcut(",", modifiers: .command)

            Divider().padding(.horizontal, 8)

            HStack {
                Text("Open visualizer")
                Spacer()
                Text(verbatim: shortcut?.description ?? "Not set")
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .foregroundStyle(.secondary)

            // One row per missing permission. A never-asked one opens the welcome window, which can
            // show the macOS prompt; a denied one can only be switched on in System Settings.
            ForEach(permissions.status.missing) { permission in
                Button {
                    dismiss()
                    permissions.resolve(permission)
                } label: {
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.yellow)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(permission.errorTitle)
                            Text(permissions.status.action(for: permission) == .showWelcome ? "Allow access ›" : "Open System Settings ›")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                    }
                }
                .buttonStyle(MenuRowButtonStyle())
            }
        }
        .padding(6)
        .frame(width: 260)
        // Re-read each time the popup appears so a changed shortcut or permission shows up.
        .onAppear {
            shortcut = KeyboardShortcuts.getShortcut(for: .openVisualizer)
            permissions.refresh()
        }
    }
}

private struct MenuRowButtonStyle: ButtonStyle {
    @State private var hovering = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .contentShape(.rect)
            .background(
                RoundedRectangle(cornerRadius: 8)
                    .fill(Color.primary.opacity(hovering || configuration.isPressed ? 0.1 : 0))
            )
            .onHover { hovering = $0 }
    }
}
