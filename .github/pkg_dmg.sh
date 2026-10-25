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
APP=TraeBar.app
BIN=".build/${ARCH}-apple-macosx/release/TraeBar"

[ -f "$BIN" ] || { echo "找不到二进制：$BIN" >&2; exit 1; }

rm -rf "$APP" dmg-stage
mkdir -p "$APP/Contents/MacOS"
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
hdiutil create -volname "TraeBar ${ARCH}" -srcfolder dmg-stage -ov -format UDZO "TraeBar-macos-${ARCH}.dmg"
rm -rf dmg-stage "$APP"
