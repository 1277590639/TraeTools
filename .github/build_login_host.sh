#!/usr/bin/env bash
# 构建 macOS 登录宿主（Swift + WKWebView）并放进发布目录。
# 用法: build_login_host.sh <arm64|x86_64> <out_dir>
set -euo pipefail

ARCH="${1:?用法: build_login_host.sh <arm64|x86_64> <out_dir>}"
OUT_DIR="${2:?缺少发布目录}"

cd "$(dirname "$0")/../macos-loginhost"
swift build -c release --arch "$ARCH"

# --arch 下产物在 .build/<triple>/release/，且同目录树里有个同名 dSYM 调试符号文件
# （不是可执行文件），必须排掉，否则会把 dSYM 当宿主打包 —— 本地不带 --arch 时没这个
# 歧义，只有 CI 能暴露。
BIN="$(find .build -type f -name LoginHost -not -path '*.dSYM/*' | head -1)"
[ -n "$BIN" ] || { echo "FAIL: 未产出宿主二进制" >&2; exit 1; }

mkdir -p "$OUT_DIR"
cp "$BIN" "$OUT_DIR/TraeTools.LoginHost"
chmod +x "$OUT_DIR/TraeTools.LoginHost"
echo "宿主: $BIN -> $OUT_DIR/TraeTools.LoginHost"
