// swift-tools-version:6.0
import PackageDescription

// macOS 版登录宿主，与 Windows 侧 TraeTools.LoginHost 平级：
// 主程序按「命令行参数 + --out 回写 JSON」的契约调用它，不关心宿主用什么语言写。
// 产物二进制在打包时重命名为 TraeTools.LoginHost（SwiftPM 的 target 名不能带点）。
let package = Package(
    name: "TraeToolsLoginHost",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "LoginHost",
            path: "Sources/LoginHost",
            swiftSettings: [.swiftLanguageMode(.v5)]
        )
    ]
)
