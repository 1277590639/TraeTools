#!/usr/bin/env bash
# macOS .app 真机冒烟：启动、等窗口、截图、校验应用名、再验证退出后能否二次启动。
# 用法: smoke_macos_app.sh <path/to/TraeTools.app> <work_dir>
#
# 判据是「窗口数」而不是「进程存活」——只判存活的话，进程挂在那儿一个窗口都没有
# 也能报绿，UI 起不来会静默溜过。
set -u

APP_BUNDLE="${1:?用法: smoke_macos_app.sh <app_bundle> <work_dir>}"
WORK="${2:?缺少工作目录}"
mkdir -p "$WORK"

fail() { echo "FAIL: $*"; exit 1; }

# 轮询而不是固定等待：runner 是 Apple Silicon，x86_64 产物走 Rosetta 转译，
# 冷启动耗时抖动很大；固定等待分不清「还没起来」和「永远起不来」。
launch() {
  local log="$1"
  open -n --stdout "$log" --stderr "$log" "$APP_BUNDLE"
  for _ in 1 2 3 4 5 6 7 8; do
    sleep 2
    PID=$(pgrep -x TraeTools | head -1)
    [ -n "$PID" ] && return 0
  done
  return 1
}

wait_window() {
  local out="$1"
  WC=0
  for i in $(seq 1 20); do
    sleep 3
    kill -0 "$PID" 2>/dev/null || fail "进程在第 $((i*3+3))s 退出（日志见 $out）"
    if osascript -e "tell application \"System Events\" to get count of windows of (first process whose unix id is $PID)" \
        > "$WORK/window-count.txt" 2>"$WORK/osascript.err"; then
      WC=$(tr -d '[:space:]' < "$WORK/window-count.txt")
    else
      WC=unknown
    fi
    echo "  t=$((i*3+3))s windows=$WC"
    # 只在拿到真实正整数窗口数时才停；unknown 继续轮询，
    # 否则一次偶发的 osascript 失败会让截图拍早。
    if [[ "$WC" =~ ^[0-9]+$ ]] && [ "$WC" -gt 0 ]; then return 0; fi
  done
  return 1
}

# ---------- 第一次启动 ----------
launch "$WORK/app.log" || { cat "$WORK/app.log"; fail "open 后 16s 内找不到 TraeTools 进程"; }
echo "pid=$PID"
wait_window "$WORK/app.log" || { cat "$WORK/app.log"; fail "轮询满 63s 仍无窗口"; }
screencapture -x "$WORK/screen.png"

# 品牌名：加粗那条是应用菜单。不能用 menu bar item 1（那是苹果 logo，实测返回 "Apple"），
# 也不能靠下标，所以取全部菜单项名字再匹配。
osascript -e "tell application \"System Events\" to get name of every menu bar item of menu bar 1 of (first process whose unix id is $PID)" \
  > "$WORK/app-menu-names.txt" 2>/dev/null
echo "菜单栏项: $(cat "$WORK/app-menu-names.txt")"
grep -q "TraeTools" "$WORK/app-menu-names.txt" || fail "菜单栏里没有 TraeTools 应用菜单"
echo "OK 应用名已生效"

echo "--- app.log ---"
cat "$WORK/app.log"
pkill -x TraeTools 2>/dev/null
sleep 2

# ---------- 退出后能否再次启动 ----------
# .NET 在 Unix 上的命名 Mutex 会落地成 /tmp 文件，若退出后残留，第二次启动会走
# createdNew=false 分支静默 return，用户表现就是「关掉再也打不开」。只能实测。
launch "$WORK/app2.log" || { cat "$WORK/app2.log"; fail "第二次启动连进程都没有"; }
echo "第二次启动 pid=$PID"
wait_window "$WORK/app2.log" || { cat "$WORK/app2.log"; fail "第二次启动没有窗口（疑似 Mutex 残留导致静默退出）"; }
echo "OK 可重复启动"
pkill -x TraeTools 2>/dev/null
exit 0
