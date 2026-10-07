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
        .testTarget(name: "IdleVizCoreTests", dependencies: ["IdleVizCore"])
    ]
)
