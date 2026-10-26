#!/usr/bin/env bash
# macOS 产物的统一验收脚本。探路 workflow 和 Release job 都调它，
# 避免两份断言各改各的、最后 release 那边悄悄放宽。
#
# 用法: verify_macos_artifacts.sh <app_dir>
#   <app_dir> 里需同时有主程序 TraeTools 和登录宿主 TraeTools.LoginHost
set -u

APP_DIR="${1:?用法: verify_macos_artifacts.sh <app_dir>}"
APP="$APP_DIR/TraeTools"
HOST="$APP_DIR/TraeTools.LoginHost"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

fail() { echo "FAIL: $*"; exit 1; }
ok()   { echo "OK $*"; }

[ -x "$APP" ]  || fail "主程序不存在或不可执行：$APP"
[ -x "$HOST" ] || fail "登录宿主不存在或不可执行：$HOST"

# ---------- 1. 登录宿主进程契约 ----------
# 主程序只靠「命令行参数 + --out 回写 JSON」和宿主通信，这几条钉死的是那份契约。
"$HOST" >/dev/null 2>&1; rc=$?
[ "$rc" = "3" ] || fail "宿主无参调用应返回 3（参数错误），实际 $rc"
ok "宿主可执行，缺参数 → exit 3"

"$HOST" --user-data-dir "$WORK/ud" --out "$WORK/st.json" --selftest >/dev/null 2>&1; rc=$?
[ "$rc" = "0" ] || fail "宿主 --selftest 应返回 0，实际 $rc"
[ -f "$WORK/st.json" ] || fail "宿主 --selftest 没写结果文件"
python3 - "$WORK/st.json" <<'PY' || exit 1
import json, sys
d = json.load(open(sys.argv[1]))
assert isinstance(d.get("token"), str) and d["token"].strip(), "token 缺失或非字符串"
assert isinstance(d.get("session"), str), "session 缺失"
assert "error" not in d, "成功形态不该带 error"
PY
ok "宿主成功回写结构符合 LoginHostClient.ParseResult"

# 代理这条必须是「显式失败」而不是静默直连：静默直连会让用户以为多账号分网了，
# 实际全走同一个出口 IP，正是触发 9074 风控的条件。
# （WKWebView 在 macOS 上做不到连接级代理，两种 ProxySettings 写法已实测否掉。）
"$HOST" --user-data-dir "$WORK/ud" --out "$WORK/px.json" --proxy-server 127.0.0.1:7890 >/dev/null 2>&1; rc=$?
[ "$rc" = "4" ] || fail "宿主不支持代理时应返回 4，实际 $rc"
python3 - "$WORK/px.json" <<'PY' || exit 1
import json, sys
d = json.load(open(sys.argv[1]))
assert isinstance(d.get("error"), str) and d["error"], "必须回写 error 而不是假装成功"
assert "token" not in d
PY
ok "代理被显式拒绝（未静默直连）"

# ---------- 2. JWT 归属解析 ----------
# 证明「页面登录了另一个账号就拒关窗」这条防线真的在解析 data.id，
# 而不是解析失败后永远走放行分支。
JWT=$(python3 -c "
import base64, json
def b64(d): return base64.urlsafe_b64encode(json.dumps(d, separators=(',',':')).encode()).decode().rstrip('=')
print(b64({'alg':'HS256'}) + '.' + b64({'data':{'id':'7654321098765432'}}) + '.sig')
")
got=$("$HOST" --user-data-dir "$WORK/ud" --out "$WORK/p.json" --jwt-uid-probe "$JWT")
[ "$got" = "7654321098765432" ] || fail "JWT 归属解析应得 7654321098765432，实际 '$got'"
got=$("$HOST" --user-data-dir "$WORK/ud" --out "$WORK/p.json" --jwt-uid-probe "not-a-jwt")
[ "$got" = "<none>" ] || fail "非 JWT 应得 <none>，实际 '$got'"
ok "JWT 归属解析与 TokenUtils.ParseAccountUid 语义一致"

# ---------- 3. 凭据加密往返 ----------
# 换加密机制后最容易「静默坏掉」：Protect 成功但 Unprotect 取不回来，
# 界面上只表现为每次启动都要重新登录。
"$APP" --crypto-selftest > "$WORK/crypto.log" 2>&1
grep -q "CRYPTO SELFTEST: PASS" "$WORK/crypto.log" || { cat "$WORK/crypto.log"; fail "凭据加密自检未通过"; }
grep -q "mechanism:" "$WORK/crypto.log" && sed -n 's/^mechanism: //p' "$WORK/crypto.log" | head -1
ok "凭据加密往返正常"

# ---------- 4. 开机自启 ----------
# plist 文件存在不等于格式合法：格式错了 launchd 静默忽略，用户端就是「勾了没反应」。
"$APP" --autostart-selftest > "$WORK/autostart.log" 2>&1
grep -q "AUTOSTART SELFTEST: PASS" "$WORK/autostart.log" || { cat "$WORK/autostart.log"; fail "自启往返自检未通过"; }
sed -n '/\[autostart\] begin-content/,/\[autostart\] end-content/p' "$WORK/autostart.log" \
  | sed '1d;$d' > "$WORK/launchagent.plist"
if [ -s "$WORK/launchagent.plist" ]; then
  plutil -lint "$WORK/launchagent.plist" >/dev/null || { cat "$WORK/launchagent.plist"; fail "LaunchAgent plist 格式非法"; }
  grep -q "/usr/bin/open" "$WORK/launchagent.plist" \
    || fail "LaunchAgent 未走 open -a：直接指向包内二进制会绕过 LaunchServices 丢掉 bundle 身份"
  ok "LaunchAgent plist 合法且走 open -a"
else
  # Windows 上没有 plist；本脚本目前只在 macOS runner 上调用，走到这里说明自检没产出。
  fail "自启处于打开态却没打印出 plist 内容"
fi

echo "=== macOS 产物验收全部通过 ==="
