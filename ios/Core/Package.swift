// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "HushCore", platforms: [.iOS(.v17), .macOS(.v13)],
    products: [.library(name: "HushCore", targets: ["HushCore"])],
    targets: [.target(name: "HushCore"), .testTarget(name: "HushCoreTests", dependencies: ["HushCore"])])
