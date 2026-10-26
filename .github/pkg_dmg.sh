#!/usr/bin/env bash
# 打包 TraeBar 为标准 macOS .dmg 安装镜像。
# 用法: pkg_dmg.sh <arm64|x86_64> <version>
#   - 从 .build/<arch>-apple-macosx/release/TraeBar 取二进制
#   - 组装 TraeBar.app/Contents（Info.plist: LSUIElement 菜单栏应用 + HiDPI）
#   - ad-hoc 签名（未公证：首次打开仍需 右键→打开 或 xattr -cr）
#   - hdiutil 产出 TraeBar-macos-<arch>.dmg（含拖入 Applications 的软链接）
set -euo pipefail

ARCH="${1:?用法: pkg_dmg.sh <arm64|x86_64> <version>}"
VERSION="${2:-dev}"
# 文件名统一用 x64（与 workflow/README 一致），构建三元组仍是 x86_64
LABEL="$ARCH"
[ "$ARCH" = "x86_64" ] && LABEL="x64"
APP=TraeBar.app
BIN=".build/${ARCH}-apple-macosx/release/TraeBar"

[ -f "$BIN" ] || { echo "找不到二进制：$BIN" >&2; exit 1; }

rm -rf "$APP" dmg-stage
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# ---- 应用图标：Swift + AppKit 现场绘制（圆角深色底 + 青色 T），生成 .icns ----
cat > make_icon.swift << 'EOF'
import AppKit
let size: CGFloat = 1024
let img = NSImage(size: NSSize(width: size, height: size))
img.lockFocus()
let bg = NSBezierPath(roundedRect: NSRect(x: 64, y: 64, width: size - 128, height: size - 128), xRadius: 200, yRadius: 200)
NSColor(calibratedWhite: 0.09, alpha: 1).setFill()
bg.fill()
if let grad = NSGradient(colors: [NSColor(calibratedWhite: 0.13, alpha: 1), NSColor(calibratedWhite: 0.05, alpha: 1)]) {
    grad.draw(in: bg, angle: -90)
}
let para = NSMutableParagraphStyle()
para.alignment = .center
let attrs: [NSAttributedString.Key: Any] = [
    .font: NSFont.systemFont(ofSize: 620, weight: .bold),
    .foregroundColor: NSColor(calibratedRed: 0.13, green: 0.83, blue: 0.92, alpha: 1),
    .paragraphStyle: para,
]
let str = NSAttributedString(string: "T", attributes: attrs)
str.draw(in: NSRect(x: 0, y: size / 2 - 460, width: size, height: 920))
img.unlockFocus()
let rep = NSBitmapImageRep(data: img.tiffRepresentation!)!
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "icon_1024.png"))
EOF
swift make_icon.swift
mkdir -p AppIcon.iconset
for s in 16 32 64 128 256 512; do
  sips -z $s $s icon_1024.png --out AppIcon.iconset/icon_${s}x${s}.png >/dev/null
  d=$((s * 2))
  sips -z $d $d icon_1024.png --out AppIcon.iconset/icon_${s}x${s}@2x.png >/dev/null
done
iconutil -c icns AppIcon.iconset -o AppIcon.icns
cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>TraeBar</string>
  <key>CFBundleDisplayName</key><string>TraeBar</string>
  <key>CFBundleIdentifier</key><string>com.traetools.traebar</string>
  <key>CFBundleExecutable</key><string>TraeBar</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION#v}</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
cp "$BIN" "$APP/Contents/MacOS/TraeBar"
chmod +x "$APP/Contents/MacOS/TraeBar"

# ad-hoc 签名（无证书时的标准做法，消除部分「已损坏」提示；公证需 Apple 开发者账号）
codesign --force --sign - "$APP"

# dmg 布局：app + Applications 软链接（拖拽安装）
mkdir -p dmg-stage
cp -R "$APP" dmg-stage/
ln -s /Applications dmg-stage/Applications
cat > "dmg-stage/首次启动必读.txt" << 'READMEE'
【TraeBar 首次启动放行（只需一次）】

macOS 会拦截一切从网络下载的未公证软件，这与软件本身无关。放行方式二选一：

方式一（推荐）：打开终端，执行：
  xattr -cr /Applications/TraeBar.app

方式二：系统设置 -> 隐私与安全性 -> 拉到底部，点击「仍要打开」。

之后即可正常双击使用。
READMEE
hdiutil create -volname "TraeBar ${LABEL}" -srcfolder dmg-stage -ov -format UDZO "TraeBar-macos-${LABEL}.dmg"
rm -rf dmg-stage "$APP" AppIcon.iconset icon_1024.png make_icon.swift
