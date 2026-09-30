import KeyboardShortcuts
import SwiftUI

/// The small glass popup under the menu-bar icon. See mockups.html, section 1.
struct MenuBarView: View {
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
        }
        .padding(6)
        .frame(width: 260)
        // Re-read each time the popup appears so a changed shortcut shows up.
        .onAppear { shortcut = KeyboardShortcuts.getShortcut(for: .openVisualizer) }
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
