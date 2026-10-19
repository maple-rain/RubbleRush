"""WebGL 빌드를 확인용으로 띄우는 최소 서버.

유니티 WebGL 은 Brotli 로 압축된 .br 파일을 내놓는데, 서버가
Content-Encoding: br 를 같이 보내지 않으면 브라우저가 압축을 못 푼다.
python -m http.server 로는 안 되는 이유가 이것이다.
"""
import http.server, socketserver, sys, functools

ENC = {".br": "br", ".gz": "gzip"}
TYPE = {".data": "application/octet-stream", ".wasm": "application/wasm",
        ".js": "application/javascript", ".json": "application/json"}

class H(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        p = self.path.split("?")[0]
        for ext, enc in ENC.items():
            if p.endswith(ext):
                self.send_header("Content-Encoding", enc)
                inner = p[: -len(ext)]
                for e2, t in TYPE.items():
                    if inner.endswith(e2):
                        self.send_header("Content-Type", t)
                        break
                break
        super().end_headers()
    def log_message(self, *a):
        pass

root, port = sys.argv[1], int(sys.argv[2])
socketserver.TCPServer.allow_reuse_address = True
with socketserver.TCPServer(("127.0.0.1", port),
                            functools.partial(H, directory=root)) as s:
    print(f"http://127.0.0.1:{port}/  <- {root}", flush=True)
    s.serve_forever()
