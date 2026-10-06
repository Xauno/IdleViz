import Foundation

/// How the visualizer is placed on more than one display.
public enum DisplayPlacement: String, Sendable, CaseIterable {
    /// Each display runs its own visualizer, with the same preset and the same audio.
    case mirror
    /// One picture runs across all the displays.
    case extend
}

/// The "Displays" settings: which display is the main one, whether further displays are covered,
/// which ones, how, and where the Spotify overlay shows. "Close on input" is kept here too, under
/// the key the Windows app gave it when it only counted with more than one display.
/// Displays are stored by their UUID, which stays the same across restarts and ports.
public struct MultiDisplaySettings: Sendable, Equatable {
    public static let mainDisplayKey = "mainDisplay"
    public static let enabledKey = "multiDisplay"
    public static let otherDisplaysKey = "multiDisplayOthers"
    public static let closeOnInputKey = "multiDisplayCloseOnInput"
    public static let placementKey = "multiDisplayPlacement"
    public static let overlayDisplayKey = "overlayDisplay"

    public static let overlayOnMain = "main"
    public static let overlayOnAll = "all"

    /// Shown while more than one display is covered.
    public static let gpuWarning =
        "Using more than one display takes more GPU power. The visualizer may run less smoothly, at a lower frame rate."
    /// Shown when "Extend across displays" is chosen while macOS gives each display its own Space.
    public static let separateSpacesWarning =
        "Extending doesn't work while \"Displays have separate Spaces\" is on in System Settings, under Desktop & Dock. "
        + "Each display runs its own visualizer instead."

    /// The main display, or nil for the one macOS calls main.
    public var mainDisplay: String?
    /// Whether more than one display is covered.
    public var enabled = false
    /// The further displays to cover, or nil for all of them, including ones plugged in later.
    public var otherDisplays: [String]?
    /// Whether input closes the visualizer, on one display or several.
    public var closeOnInput = true
    public var placement = DisplayPlacement.mirror
    /// `overlayOnMain`, `overlayOnAll` or a display's UUID.
    public var overlayDisplay = MultiDisplaySettings.overlayOnMain

    public init(
        mainDisplay: String? = nil,
        enabled: Bool = false,
        otherDisplays: [String]? = nil,
        closeOnInput: Bool = true,
        placement: DisplayPlacement = .mirror,
        overlayDisplay: String = MultiDisplaySettings.overlayOnMain
    ) {
        self.mainDisplay = mainDisplay
        self.enabled = enabled
        self.otherDisplays = otherDisplays
        self.closeOnInput = closeOnInput
        self.placement = placement
        self.overlayDisplay = overlayDisplay
    }

    /// Reads the stored settings. Missing or unusable values fall back to the defaults.
    public init(defaults: UserDefaults) {
        self.init()
        if let main = defaults.string(forKey: Self.mainDisplayKey), !main.isEmpty { mainDisplay = main }
        enabled = defaults.object(forKey: Self.enabledKey) as? Bool ?? false
        otherDisplays = defaults.stringArray(forKey: Self.otherDisplaysKey)
        closeOnInput = defaults.object(forKey: Self.closeOnInputKey) as? Bool ?? true
        placement = defaults.string(forKey: Self.placementKey).flatMap(DisplayPlacement.init(rawValue:)) ?? .mirror
        if let overlay = defaults.string(forKey: Self.overlayDisplayKey), !overlay.isEmpty { overlayDisplay = overlay }
    }

    /// The main display among the connected ones (macOS's main one first): the stored one, or the first.
    public func main(among displays: [Display]) -> Display? {
        displays.first { $0.uuid == mainDisplay } ?? displays.first
    }

    /// Whether a display other than the main one is covered while the switch is on.
    public func covers(_ display: Display) -> Bool {
        otherDisplays?.contains(display.uuid) ?? true
    }
}

/// Names the displays in the settings window.
public enum DisplayLabel {
    /// The name macOS gives each display ("Built-in Retina Display", "DELL U2720Q"), in the order
    /// given. Displays that share a name are numbered, "DELL U2720Q (2)", and one that reports no
    /// name is "Display 2 (2560 × 1440)".
    public static func labels(for displays: [Display]) -> [String] {
        var seen: [String: Int] = [:]
        return displays.enumerated().map { index, display in
            let name = display.model?.trimmingCharacters(in: .whitespaces) ?? ""
            guard !name.isEmpty else {
                return "Display \(index + 1) (\(Int(display.frame.width)) × \(Int(display.frame.height)))"
            }
            seen[name, default: 0] += 1
            let count = seen[name] ?? 1
            return count == 1 ? name : "\(name) (\(count))"
        }
    }

    /// What a stored display that isn't connected right now is called.
    public static let notConnected = "Display not connected"
}

/// One display's part of a visualizer window, in points from the window's top left corner.
public struct DisplayRegion: Sendable, Equatable {
    public var left: Int
    public var top: Int
    public var width: Int
    public var height: Int
    public var overlay: Bool

    public init(left: Int, top: Int, width: Int, height: Int, overlay: Bool) {
        self.left = left
        self.top = top
        self.width = width
        self.height = height
        self.overlay = overlay
    }
}

/// One visualizer window: where it goes and the displays it covers.
public struct PlannedWindow: Sendable, Equatable {
    /// The window's frame in screen coordinates, as AppKit counts them: in points, with y going up.
    public var frame: CGRect
    /// The displays it covers, the main one first.
    public var regions: [DisplayRegion]
    /// The widest the page may render the visualizer.
    public var renderWidthCap: Int

    public init(frame: CGRect, regions: [DisplayRegion], renderWidthCap: Int) {
        self.frame = frame
        self.regions = regions
        self.renderWidthCap = renderWidthCap
    }

    /// The calls that tell the page in this window which displays it covers. The page scales the
    /// regions by the window's width, so points do as well as pixels.
    public var script: String {
        let regions = regions.map { region in
            "{\"height\":\(region.height),\"overlay\":\(region.overlay),\"width\":\(region.width),\"x\":\(region.left),\"y\":\(region.top)}"
        }
        let layout = "{\"height\":\(Int(frame.height)),\"regions\":[\(regions.joined(separator: ","))],\"width\":\(Int(frame.width))}"
        return "window.setRenderWidthCap?.(\(renderWidthCap));window.setOverlayRegions?.(\(layout))"
    }
}

/// Which visualizer windows to show for the settings and the displays connected right now. The
/// first window is the main one: it holds the page that picks the presets, and the others follow it.
public struct DisplayPlan: Sendable, Equatable {
    /// The page's own render width cap (`MAX_RENDER_WIDTH` in visualizer-state.js).
    public static let renderWidthCap = 2560
    /// The page takes no cap above this (`MAX_SPAN_RENDER_WIDTH`).
    public static let maxRenderWidthCap = 7680

    /// What to plan for when macOS reports no display at all.
    private static let noDisplay = Display(id: 0, frame: CGRect(x: 0, y: 0, width: 1920, height: 1080), scale: 1)

    /// The windows, the main one first.
    public var windows: [PlannedWindow]
    /// False when input leaves the visualizer open; a second manual trigger closes it then.
    public var closeOnInput: Bool

    public init(windows: [PlannedWindow], closeOnInput: Bool) {
        self.windows = windows
        self.closeOnInput = closeOnInput
    }

    /// - Parameters:
    ///   - displays: The connected displays, macOS's main one first.
    ///   - canSpan: Whether one window can run across displays. It can't while "Displays have
    ///     separate Spaces" is on, so "Extend across displays" then gives each display its own window.
    public init(settings: MultiDisplaySettings, displays: [Display], canSpan: Bool) {
        let displays = displays.isEmpty ? [Self.noDisplay] : displays
        let main = settings.main(among: displays) ?? Self.noDisplay
        var covered = [main]
        if settings.enabled {
            covered += displays.filter { $0 != main && settings.covers($0) }
        }

        // A display that isn't covered can't show the overlay, so it falls back to the main one.
        let overlayOnAll = settings.overlayDisplay == MultiDisplaySettings.overlayOnAll
        let overlayOn = covered.contains { $0.uuid == settings.overlayDisplay } ? settings.overlayDisplay : main.uuid
        func overlay(_ display: Display) -> Bool { overlayOnAll || display.uuid == overlayOn }

        closeOnInput = settings.closeOnInput
        guard covered.count > 1, settings.placement == .extend, canSpan else {
            windows = covered.map { display in
                let size = display.frame.size
                let region = DisplayRegion(left: 0, top: 0, width: Int(size.width), height: Int(size.height), overlay: overlay(display))
                return PlannedWindow(frame: display.frame, regions: [region], renderWidthCap: Self.renderWidthCap)
            }
            return
        }

        // One window over the rectangle that encloses the displays. Parts of it that no display covers aren't seen.
        let span = covered.dropFirst().reduce(main.frame) { $0.union($1.frame) }
        // The main display's part stays as sharp as when it is covered alone.
        let cap = min((Double(Self.renderWidthCap) * span.width / main.frame.width).rounded(), Double(Self.maxRenderWidthCap))
        let regions = covered.map { display in
            DisplayRegion(
                left: Int(display.frame.minX - span.minX),
                // AppKit counts y upwards; the page counts down from the top.
                top: Int(span.maxY - display.frame.maxY),
                width: Int(display.frame.width),
                height: Int(display.frame.height),
                overlay: overlay(display)
            )
        }
        windows = [PlannedWindow(frame: span, regions: regions, renderWidthCap: Int(cap))]
    }
}
