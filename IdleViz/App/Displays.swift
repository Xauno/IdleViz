import AppKit
import IdleVizCore

extension DisplayLayout {
    /// The displays as they are right now.
    @MainActor static var current: DisplayLayout {
        #if DEBUG
        if Displays.splitMainDisplay, let screen = NSScreen.screens.first { return split(screen) }
        #endif
        return DisplayLayout(displays: NSScreen.screens.map { screen in
            let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber
            let id = number?.uint32Value ?? 0
            return Display(
                id: id,
                frame: screen.frame,
                scale: screen.backingScaleFactor,
                uuid: Displays.uuid(of: id),
                model: screen.localizedName
            )
        })
    }
}

#if DEBUG
extension DisplayLayout {
    /// The main display as two: its left and its right half.
    @MainActor private static func split(_ screen: NSScreen) -> DisplayLayout {
        let (left, right) = screen.frame.divided(atDistance: (screen.frame.width / 2).rounded(), from: .minXEdge)
        return DisplayLayout(displays: [
            Display(id: 1, frame: left, scale: screen.backingScaleFactor, uuid: "LEFT-HALF", model: "Left half"),
            Display(id: 2, frame: right, scale: screen.backingScaleFactor, uuid: "RIGHT-HALF", model: "Right half"),
        ])
    }
}
#endif

@MainActor
enum Displays {
    #if DEBUG
    /// Launch argument `-IdleVizSplitDisplay YES` treats the two halves of the main display as two
    /// displays, for working on more than one display with only one connected.
    static let splitMainDisplay = UserDefaults.standard.bool(forKey: "IdleVizSplitDisplay")
    #endif

    /// Whether one window can run across displays. With "Displays have separate Spaces" on in
    /// System Settings (the default), macOS shows a window on one display only.
    static var canSpan: Bool {
        #if DEBUG
        if splitMainDisplay { return true }
        #endif
        return !NSScreen.screensHaveSeparateSpaces
    }

    /// The windows to show for the stored settings and the displays connected right now.
    static var plan: DisplayPlan {
        DisplayPlan(settings: MultiDisplaySettings(defaults: .standard), displays: DisplayLayout.current.displays, canSpan: canSpan)
    }

    /// The display's UUID, or an empty string for a display macOS has none for.
    nonisolated static func uuid(of id: CGDirectDisplayID) -> String {
        guard let uuid = CGDisplayCreateUUIDFromDisplayID(id)?.takeRetainedValue() else { return "" }
        return CFUUIDCreateString(nil, uuid) as String
    }
}
