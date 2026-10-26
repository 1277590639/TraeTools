#!/usr/bin/env python3
"""假 Trae 登录页：把 token 写进 localStorage，把 session 通过 Set-Cookie 下发。

用于端到端验证 macOS 登录宿主的凭据捕获链路，不需要真实 Trae 账号。
用法: fake_trae_page.py <port> <jwt> <session>
"""
import base64
import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

JWT = sys.argv[2]
SESSION = sys.argv[3]

PAGE = f"""<!doctype html><html><head><meta charset="utf-8"><title>fake trae</title></head>
<body><script>
localStorage.setItem('Cloud-IDE-Token', {json.dumps(JWT)});
</script><p>fake trae login page</p></body></html>"""


class H(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        # 宿主靠这个 cookie 拿 X-Cloudide-Session（约 14 天续命的关键）
        self.send_header("Set-Cookie", f"X-Cloudide-Session={SESSION}; Path=/")
        self.end_headers()
        self.wfile.write(PAGE.encode())

    def log_message(self, *a):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1])), H).serve_forever()
