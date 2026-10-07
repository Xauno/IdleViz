// Draws the Mac app icon and the disk image's background, and writes them where the build expects them:
//   IdleViz/AppIcon.icns   the menu-bar waveform, white on a dark rounded square (the Windows icon's design)
//   installer/background.png   the window behind the two icons in IdleViz.dmg
// Both files are committed; run this again only to change the design:
//   cd mac && swift installer/make-assets.swift
import AppKit

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let work = FileManager.default.temporaryDirectory.appendingPathComponent("idleviz-assets-\(UUID().uuidString)")
try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: work) }

func png(width: Int, height: Int, scale: CGFloat = 1, draw: (CGContext) -> Void) -> Data {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height, bitsPerSample: 8, samplesPerPixel: 4,
        hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!
    // The size in points, so a 2x image is marked as such.
    rep.size = NSSize(width: CGFloat(width) / scale, height: CGFloat(height) / scale)
    let context = NSGraphicsContext(bitmapImageRep: rep)!
    NSGraphicsContext.current = context
    context.cgContext.scaleBy(x: scale, y: scale)
    draw(context.cgContext)
    context.flushGraphics()
    NSGraphicsContext.current = nil
    return rep.representation(using: .png, properties: [:])!
}

func run(_ tool: String, _ arguments: [String]) throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: tool)
    process.arguments = arguments
    try process.run()
    process.waitUntilExit()
    guard process.terminationStatus == 0 else { fatalError("\(tool) failed") }
}

// MARK: App icon

/// The waveform on a 16 x 16 grid: x, top and bottom of each bar. Same shape as the menu-bar and tray glyph.
let bars: [(x: CGFloat, top: CGFloat, bottom: CGFloat)] = [(2, 7, 9), (5, 4, 12), (8, 2, 14), (11, 5, 11), (14, 7, 9)]

func drawIcon(_ context: CGContext, size: CGFloat) {
    // Apple's icon grid: the square takes 824 of 1024 points, with the rest left for its shadow.
    let side = size * 824 / 1024
    let square = CGRect(x: (size - side) / 2, y: (size - side) / 2, width: side, height: side)
    let path = CGPath(roundedRect: square, cornerWidth: side * 0.225, cornerHeight: side * 0.225, transform: nil)
    context.saveGState()
    context.setShadow(offset: CGSize(width: 0, height: -size * 0.01), blur: size * 0.02, color: CGColor(gray: 0, alpha: 0.3))
    context.setFillColor(CGColor(red: 27 / 255, green: 27 / 255, blue: 27 / 255, alpha: 1))
    context.addPath(path)
    context.fillPath()
    context.restoreGState()

    // The glyph fills the middle 62% of the square. Its grid counts down from the top; Core Graphics counts up.
    let scale = side * 0.62 / 16
    let left = square.minX + (side - 16 * scale) / 2
    let top = square.maxY - (side - 16 * scale) / 2
    context.setStrokeColor(CGColor(gray: 1, alpha: 1))
    context.setLineWidth(1.7 * scale)
    context.setLineCap(.round)
    for bar in bars {
        context.move(to: CGPoint(x: left + bar.x * scale, y: top - bar.top * scale))
        context.addLine(to: CGPoint(x: left + bar.x * scale, y: top - bar.bottom * scale))
    }
    context.strokePath()
}

let iconset = work.appendingPathComponent("AppIcon.iconset")
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for points in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = points * scale
        let name = scale == 1 ? "icon_\(points)x\(points).png" : "icon_\(points)x\(points)@2x.png"
        try png(width: pixels, height: pixels) { drawIcon($0, size: CGFloat(pixels)) }.write(to: iconset.appendingPathComponent(name))
    }
}
try run("/usr/bin/iconutil", ["-c", "icns", iconset.path, "-o", root.appendingPathComponent("IdleViz/AppIcon.icns").path])

// MARK: Disk image background

/// The window is 600 x 400 points. The app's icon sits at (150, 170) and the Applications folder's
/// at (450, 170), counted from the top left as Finder does; see dmg-settings.py.
func drawBackground(_ context: CGContext) {
    let width: CGFloat = 600, height: CGFloat = 400
    // A mid grey: Finder writes the icons' names in black in light mode and in white in dark mode,
    // whatever is behind them, and both have to stay readable.
    context.setFillColor(CGColor(red: 110 / 255, green: 110 / 255, blue: 115 / 255, alpha: 1))
    context.fill(CGRect(x: 0, y: 0, width: width, height: height))

    // The arrow between the two icons, level with their middles.
    let y = height - 170
    context.setStrokeColor(CGColor(gray: 1, alpha: 0.8))
    context.setLineWidth(4)
    context.setLineCap(.round)
    context.setLineJoin(.round)
    context.move(to: CGPoint(x: 258, y: y))
    context.addLine(to: CGPoint(x: 342, y: y))
    context.move(to: CGPoint(x: 326, y: y + 16))
    context.addLine(to: CGPoint(x: 342, y: y))
    context.addLine(to: CGPoint(x: 326, y: y - 16))
    context.strokePath()

    func text(_ string: String, size: CGFloat, weight: NSFont.Weight, alpha: CGFloat, fromTop: CGFloat) {
        let style = NSMutableParagraphStyle()
        style.alignment = .center
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: size, weight: weight),
            .foregroundColor: NSColor(white: 1, alpha: alpha),
            .paragraphStyle: style,
        ]
        NSString(string: string).draw(in: CGRect(x: 20, y: height - fromTop - size * 1.4, width: width - 40, height: size * 1.4), withAttributes: attributes)
    }
    text("Drag IdleViz to Applications", size: 20, weight: .semibold, alpha: 1, fromTop: 30)
    text("The first time you open it, macOS blocks it. Allow it under", size: 12, weight: .regular, alpha: 0.9, fromTop: 318)
    text("System Settings → Privacy & Security → Open Anyway.", size: 12, weight: .regular, alpha: 0.9, fromTop: 336)
}

// One picture at 1x. Finder shows a background pixel for point and ignores a 2x version (tried on
// macOS 26 with a two-size TIFF and with a 144 dpi PNG: both came out twice the size of the window).
try png(width: 600, height: 400, draw: drawBackground).write(to: root.appendingPathComponent("installer/background.png"))
print("Wrote IdleViz/AppIcon.icns and installer/background.png")
