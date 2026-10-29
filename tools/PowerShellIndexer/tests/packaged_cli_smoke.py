"""Installed CLI -> isolated worker -> authenticated HTTP publication, using synthetic sources.

The in-process HTTP fixture checks transport and cache behavior; it is not Neo4j acceptance.
Usage: python packaged_cli_smoke.py /absolute/path/to/codemeridian[.exe]
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading


cli = Path(sys.argv[1]).resolve(strict=True)
state = {"generation": 0, "profile": None, "graphs": [], "outcomes": [], "fail": False}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def reply(self, value, status=200):
        payload = json.dumps(value).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        assert self.headers.get("Authorization") == "Bearer package-key"
        self.reply({"contractVersion": "1.0", "status": "available", "project": "PackageFixture",
                    "generation": state["generation"], "latestGeneration": state["generation"],
                    "profile": state["profile"], "warnings": []})

    def do_POST(self):
        assert self.headers.get("Authorization") == "Bearer package-key"
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            chunks = []
            while True:
                size = int(self.rfile.readline().split(b";", 1)[0], 16)
                if size == 0:
                    while self.rfile.readline() not in (b"\r\n", b"\n", b""):
                        pass
                    break
                chunks.append(self.rfile.read(size))
                assert self.rfile.read(2) == b"\r\n"
            data = json.loads(b"".join(chunks))
        else:
            data = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        if self.path.endswith("/begin"):
            state["generation"] += 1
        elif self.path == "/api/v1/project-profiles/":
            state["profile"] = data["profile"]
        elif self.path.endswith("/outcomes"):
            state["outcomes"].extend(data["outcomes"])
        elif self.path.endswith("/powershell"):
            if state["fail"]:
                self.reply({}, 503)
                return
            assert data["generation"] == state["generation"]
            assert data["sourceFingerprint"] == state["profile"]["sourceFingerprint"]
            state["graphs"].append(data)
        else:
            raise AssertionError("Unexpected endpoint: " + self.path)
        self.reply({"generation": state["generation"]})


server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
thread = threading.Thread(target=server.serve_forever, daemon=True)
thread.start()
try:
    with tempfile.TemporaryDirectory(prefix="codemeridian-cli-å ") as directory:
        root = Path(directory)
        marker = root / "executed.txt"
        (root / "meridian.json").write_text(json.dumps({"version": 2, "project": "PackageFixture",
            "indexing": {"powershell": {"enabled": True, "caseSensitive": os.name != "nt"}}}), encoding="utf-8")
        (root / "helper.ps1").write_text("function Work {};", encoding="utf-8")
        (root / "main.ps1").write_text('. "$PSScriptRoot/helper.ps1"; Work; '
            '[IO.File]::WriteAllText("$PSScriptRoot/executed.txt", "bad")', encoding="utf-8")
        (root / "node_modules").mkdir()
        (root / "node_modules" / "excluded.ps1").write_text("function {", encoding="utf-8")
        env = os.environ.copy()
        env.update({"PATH": "", "CodeMeridian_Auth_ApiKey": "package-key",
                    "CODEMERIDIAN_CONFIG_HOME": str(root / "no-global-config"),
                    "PSModulePath": str(root / "no-modules")})
        arguments = [str(cli), "index", str(root), "--url", f"http://127.0.0.1:{server.server_port}",
                     "--project", "PackageFixture", "--skip-csharp", "--skip-typescript", "--skip-config",
                     "--skip-docs", "--skip-diagnostics", "--skip-keywords"]

        def run(extra=(), expected=0):
            result = subprocess.run([*arguments, *extra], cwd=root, env=env, text=True,
                                    encoding="utf-8", capture_output=True, timeout=60)
            assert result.returncode == expected, (result.returncode, result.stdout, result.stderr,
                state["outcomes"][-2:], [(file["path"], file["status"]) for file in state["graphs"][-1]["files"]] if state["graphs"] else [])
            assert not marker.exists(), "Analyzed source executed"
            return result.stdout

        assert "PowerShell" in run(["--dry-run"])
        assert state["generation"] == 0, "Dry run contacted publication"
        run()
        graph = state["graphs"][-1]
        assert {file["path"] for file in graph["files"]} == {"helper.ps1", "main.ps1"}
        assert any(edge["type"] == 1 for file in graph["files"] for edge in file["edges"]), "No Calls edge"
        assert state["outcomes"][-1]["coverage"] == "complete"
        assert next(item for item in state["profile"]["analyzers"] if item["id"] == "powershell")["supportedCapabilities"] == ["powershell_definitions"]
        cached = list((root / ".meridian" / "cache").glob("indexer-files-*.json"))
        assert len(cached) == 1
        # Each invocation is a fresh installed process reading the persisted cache.
        generation = state["generation"]
        output = run()
        assert state["generation"] > generation, "PowerShell no-change pass must still rebind"
        assert "cache has no matching current server profile" not in output, "Verified cache identity was rejected"
        generation = state["generation"]
        # Old profiles remain readable, but cannot authorize last-good cache reuse.
        del state["profile"]["sourceFingerprint"]
        output = run()
        assert state["generation"] > generation, "Legacy source-unbound profile reused cache"
        assert "cache has no matching current server profile" in output, "Legacy profile passed cache reconciliation"
        generation = state["generation"]
        next(item for item in state["profile"]["analyzers"] if item["id"] == "powershell")["fingerprint"] = None
        output = run()
        assert state["generation"] > generation, "Unknown producer identity reused cache"
        assert "cache has no matching current server profile" in output, "Missing producer passed cache reconciliation"
        (root / "helper.ps1").write_text("function Renamed {};", encoding="utf-8")
        run()
        assert not any(edge["type"] == 1 for file in state["graphs"][-1]["files"] for edge in file["edges"])
        cache_bytes = cached[0].read_bytes()
        state["fail"] = True
        (root / "helper.ps1").write_text("function Again {};", encoding="utf-8")
        run(expected=1)
        assert cached[0].read_bytes() == cache_bytes, "Failed transport advanced cache"
        state["fail"] = False
        (root / "helper.ps1").unlink()
        run(expected=2)
        assert {file["path"] for file in state["graphs"][-1]["files"]} == {"main.ps1"}
        assert state["outcomes"][-1]["coverage"] == "partial"
        assert cached[0].read_bytes() == cache_bytes, "Partial parse/binding advanced cache"
        before = len(state["graphs"])
        run(["--skip-powershell"])
        assert len(state["graphs"]) == before, "Skip flag dispatched worker"
        print(json.dumps({"platform": sys.platform, "cliSmoke": "passed", "database": "HTTP fixture"}))
finally:
    server.shutdown()
    server.server_close()
