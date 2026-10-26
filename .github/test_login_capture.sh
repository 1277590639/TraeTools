#!/usr/bin/env bash
# 端到端验证 macOS 登录宿主的凭据捕获链路（不需要真实 Trae 账号）。
# 用法: test_login_capture.sh <path/to/TraeTools.LoginHost>
#
# 覆盖三条真实会出错的路径：
#   A 正常登录      → 抓到 token + session 并回写，进程正常退出
#   B 跨账号污染    → 拒收、不关窗、不回写（Windows 侧 P0-1 的同一条防线）
#   C 没有 session  → token 仍要拿到，session 为空（对应「8 小时就失效」告警）
set -u

HOST_BIN="${1:?用法: test_login_capture.sh <path/to/TraeTools.LoginHost>}"
[ -x "$HOST_BIN" ] || { echo "FAIL: 宿主不可执行：$HOST_BIN"; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
trap 'for j in $(jobs -p); do kill "$j" 2>/dev/null; done; wait 2>/dev/null; rm -rf "$WORK"' EXIT

PORT=8931
UID_MATCH="7654321098765432"
UID_OTHER="1111222233334444"
SESSION="sess-abcdef0123456789"

mkjwt() {
  python3 -c "
import base64, json, sys
def b(d): return base64.urlsafe_b64encode(json.dumps(d, separators=(',',':')).encode()).decode().rstrip('=')
print(b({'alg':'HS256','typ':'JWT'}) + '.' + b({'data':{'id':sys.argv[1]}}) + '.' + b({'sig':'x'*40}))
" "$1"
}
JWT_MATCH="$(mkjwt "$UID_MATCH")"
# token 归属是另一个账号，但页面本身照常返回 —— 这正是「残留会话自动续期出他人 token」的复现
JWT_OTHER="$(mkjwt "$UID_OTHER")"

start_server() {   # $1 = jwt  $2 = session（空串则不下发 cookie）
  if [ -z "$2" ]; then
    python3 - "$PORT" "$1" "unused" <<'PY' &
import sys, base64, json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
jwt = sys.argv[2]
page = ("<!doctype html><html><body><script>localStorage.setItem('Cloud-IDE-Token', %s);</script>"
        "</body></html>" % json.dumps(jwt))
class H(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200); self.send_header("Content-Type","text/html; charset=utf-8")
        self.end_headers(); self.wfile.write(page.encode())
    def log_message(self,*a): pass
ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1])), H).serve_forever()
PY
  else
    python3 "$SCRIPT_DIR/fake_trae_page.py" "$PORT" "$1" "$2" &
  fi
  SRV=$!
  sleep 1
  [ -d /dev/tcp/127.0.0.1/$PORT ] 2>/dev/null || kill -0 $SRV 2>/dev/null || { echo "FAIL: 假页面起不来"; exit 1; }
}

fail() { echo "FAIL: $*"; exit 1; }

# 后台 python 被信号终止时 bash 会把作业状态打到 stderr，混进判定输出里容易被误读成报错。
stop() { { kill "$1" 2>/dev/null; wait "$1" 2>/dev/null; } 2>/dev/null; }

# ---------- A 正常登录 ----------
echo "--- A: 正常登录 ---"
start_server "$JWT_MATCH" "$SESSION"
"$HOST_BIN" --user-data-dir "$WORK/udA" --out "$WORK/A.json" \
  --expected-uid "$UID_MATCH" --start-url "http://127.0.0.1:$PORT/" >/dev/null 2>&1
rc=$?
stop $SRV
[ "$rc" = "0" ] || { cat "$WORK/A.json" 2>/dev/null; fail "A: 应返回 0，实际 $rc"; }
[ -f "$WORK/A.json" ] || fail "A: 没写结果文件"
cat "$WORK/A.json"
python3 -c "
import json,sys
d=json.load(open('$WORK/A.json'))
assert d.get('token')=='$JWT_MATCH', 'token 与页面写入的不一致（可能被 JSON 多包了一层引号）'
assert d.get('session')=='$SESSION', 'session 没抓到或值不对'
assert 'error' not in d
print('A OK: token 与 session 都正确捕获并回写')" || exit 1

# ---------- B 跨账号污染 ----------
echo "--- B: 页面返回的是别人的 token ---"
start_server "$JWT_OTHER" "$SESSION"
"$HOST_BIN" --user-data-dir "$WORK/udB" --out "$WORK/B.json" \
  --expected-uid "$UID_MATCH" --start-url "http://127.0.0.1:$PORT/" >/dev/null 2>&1 &
HP=$!
sleep 8
if ! kill -0 $HP 2>/dev/null; then
  stop $SRV
  fail "B: 归属不符时宿主自己关窗退出了（就是「一闪而过」那个 bug）"
fi
if [ -f "$WORK/B.json" ]; then
  stop $SRV; stop $HP
  fail "B: 归属不符却仍回写了结果 = 跨账号凭据污染"
fi
stop $HP; stop $SRV
wait $HP 2>/dev/null
echo "B OK: 归属不符 → 不关窗、不回写"

# ---------- C 没有 session ----------
echo "--- C: 页面不下发 X-Cloudide-Session ---"
start_server "$JWT_MATCH" ""
"$HOST_BIN" --user-data-dir "$WORK/udC" --out "$WORK/C.json" \
  --expected-uid "$UID_MATCH" --start-url "http://127.0.0.1:$PORT/" >/dev/null 2>&1
rc=$?
stop $SRV
[ "$rc" = "0" ] || fail "C: 应仍返回 0，实际 $rc"
cat "$WORK/C.json"
python3 -c "
import json
d=json.load(open('$WORK/C.json'))
assert d.get('token'), 'C: token 丢了'
assert d.get('session') in (None,''), 'C: 不该抓到 session'
print('C OK: 无 session 时仍拿到 token，session 为空')" || exit 1

echo "=== 登录捕获链路端到端验证通过 ==="
