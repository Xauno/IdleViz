import IdleVizCore
import SwiftUI

/// The Audio sync section in settings: the delay for the current speakers or headphones, with
/// the slider, Detect and the manual delay test folded into its row.
struct AudioDelayControls: View {
    @Bindable var audioDelay: AudioDelayController
    @State private var showTest = false
    @State private var isOpen = FoldedRows.startOpen

    var body: some View {
        FoldedRow(isOpen: $isOpen) {
            // Under its label, so the slider runs the width of the row: 250 steps need the room.
            VStack(alignment: .leading, spacing: 6) {
                Text("Set by hand")
                Slider(value: $audioDelay.delay, in: AudioDelaySetting.range)
                    .labelsHidden()
                    .frame(maxWidth: .infinity)
                    .accessibilityLabel("Audio delay")
            }
            LabeledContent {
                Button(audioDelay.detecting ? "Listening…" : "Detect") { audioDelay.detect() }
                    .disabled(audioDelay.detecting)
                    .accessibilityLabel("Detect delay")
            } label: {
                Text("Detect with the microphone")
                switch audioDelay.hint {
                case let .text(text):
                    Text(text)
                case .microphoneDenied:
                    // The microphone is optional, so a missing permission only shows here.
                    Link("Microphone access is off. Open System Settings…", destination: AudioDelayController.microphoneSettingsURL)
                        .font(.caption)
                case nil:
                    Text("Listens for a few seconds")
                }
            }
            LabeledContent {
                Button("Start") { showTest = true }
                    .disabled(audioDelay.detecting)
                    .accessibilityLabel("Start the manual delay test")
            } label: {
                Text("Manual delay test")
                Text("Match a flash to a beep, no mic")
            }
            .sheet(isPresented: $showTest) {
                ManualDelaySheet(audioDelay: audioDelay)
            }
        } label: {
            LabeledContent {
                Text(AudioDelaySetting.label(audioDelay.delay)).monospacedDigit()
            } label: {
                Text("Audio delay")
                Text("For \(audioDelay.deviceName)").lineLimit(1)
            }
        }
    }
}

/// The manual delay test: beeps play through the speakers, the panel lights up one audio delay
/// after each, and the slider is dragged until the two land together.
struct ManualDelaySheet: View {
    @Bindable var audioDelay: AudioDelayController
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 10) {
            HStack {
                Text("Manual delay test").font(.headline)
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            // Redrawn every frame; the flash is worked out from the clock the beeps are scheduled on.
            TimelineView(.animation) { _ in
                RoundedRectangle(cornerRadius: 10)
                    .fill(color(for: audioDelay.testFlash))
                    .frame(height: 120)
            }
            .accessibilityHidden(true)
            if let problem = audioDelay.testProblem {
                Text(problem).font(.callout).foregroundStyle(.red)
            }
            Text("Drag until the panel lights up exactly when you hear the beep. Every fourth beep is higher, and its flash is orange.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 6) {
                Button("−10 ms") { audioDelay.delay -= AudioDelaySetting.step }
                Slider(value: $audioDelay.delay, in: AudioDelaySetting.range)
                    .accessibilityLabel("Audio delay")
                Button("+10 ms") { audioDelay.delay += AudioDelaySetting.step }
            }
            HStack {
                Text("For \(audioDelay.deviceName)").lineLimit(1).foregroundStyle(.secondary)
                Spacer()
                Text(AudioDelaySetting.label(audioDelay.delay)).monospacedDigit()
            }
            .font(.callout)
        }
        .padding(14)
        .frame(width: 320)
        .onAppear { audioDelay.startTest() }
        .onDisappear { audioDelay.stopTest() }
    }

    /// Dark between flashes, white for a beep and orange for the marked one. Fixed colours, so the
    /// flash reads the same in light and dark mode.
    private func color(for flash: BeepTest.Flash?) -> Color {
        guard let flash else { return .black }
        return flash.accent ? .orange : .white
    }
}
