// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "IdleVizCore",
    platforms: [.macOS(.v26)],
    products: [
        .library(name: "IdleVizCore", targets: ["IdleVizCore"])
    ],
    targets: [
        .target(name: "IdleVizCore"),
        // `tests/` also holds the JavaScript suite; macOS folders are case-insensitive, so name it exactly.
        .testTarget(name: "IdleVizCoreTests", dependencies: ["IdleVizCore"], path: "tests/IdleVizCoreTests")
    ]
)
