"""Local-only, rate-limited media fixture for emulator transfer/resume checks."""
import argparse
import http.server
import pathlib
import re
import time

parser = argparse.ArgumentParser()
parser.add_argument("file", type=pathlib.Path)
parser.add_argument("--port", type=int, default=8128)
args = parser.parse_args()
fixture = args.file.resolve(strict=True)


class Handler(http.server.BaseHTTPRequestHandler):
    def do_HEAD(self):
        self.serve(False)

    def do_GET(self):
        self.serve(True)

    def serve(self, body):
        if self.path != "/fixture.mp4":
            self.send_error(404)
            return
        size = fixture.stat().st_size
        start = 0
        range_header = self.headers.get("Range")
        if range_header:
            match = re.fullmatch(r"bytes=(\d+)-", range_header)
            if not match or int(match[1]) >= size:
                self.send_response(416)
                self.send_header("Content-Range", f"bytes */{size}")
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            start = int(match[1])
        self.send_response(206 if range_header else 200)
        self.send_header("Content-Type", "video/mp4")
        self.send_header("Content-Length", str(size - start))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("ETag", '"lumefetch-generated-fixture-v1"')
        if range_header:
            self.send_header("Content-Range", f"bytes {start}-{size - 1}/{size}")
        self.end_headers()
        if not body:
            return
        try:
            with fixture.open("rb") as source:
                source.seek(start)
                while data := source.read(32768):
                    self.wfile.write(data)
                    self.wfile.flush()
                    time.sleep(0.15)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass


with http.server.ThreadingHTTPServer(("127.0.0.1", args.port), Handler) as server:
    print(f"Serving only {fixture.name} on 127.0.0.1:{args.port}/fixture.mp4", flush=True)
    server.serve_forever()
