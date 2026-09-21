#!/usr/bin/env python3
"""Local-only team test server. Standard library; no package install."""
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from pathlib import Path
import os
root=Path(__file__).resolve().parents[2]/'Builds/RunnerCircleWeb'
os.chdir(root)
class Handler(SimpleHTTPRequestHandler):
    def end_headers(self):
        if self.path.endswith('.gz'):self.send_header('Content-Encoding','gzip')
        if self.path.endswith('.br'):self.send_header('Content-Encoding','br')
        self.send_header('Cache-Control','no-store');super().end_headers()
    def guess_type(self,path):
        p=path.removesuffix('.gz').removesuffix('.br')
        if p.endswith('.wasm'):return 'application/wasm'
        if p.endswith('.data'):return 'application/octet-stream'
        return super().guess_type(p)
print('AFTERECHO runner test: http://127.0.0.1:8777',flush=True)
ThreadingHTTPServer(('127.0.0.1',8777),Handler).serve_forever()
