"""Real server + installed CLI + REST/MCP against an explicitly isolated loopback database.

Usage: python fresh_process_client_smoke.py <server.dll> <installed-cli> <bolt-uri>
Requires dotnet on PATH and NEO4J__USERNAME/NEO4J__PASSWORD; never loads repository .env.
Creates only synthetic sources. This smoke does not certify the full E03 release matrix.
"""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlparse
from urllib.request import Request, urlopen
import uuid


server_dll, cli = (str(Path(value).resolve(strict=True)) for value in sys.argv[1:3])
bolt_uri = sys.argv[3]
assert urlparse(bolt_uri).hostname in ("127.0.0.1", "localhost"), "Use a dedicated loopback acceptance instance"
assert os.environ.get("NEO4J__PASSWORD"), "Explicit isolated database credentials are required"
project = "Acceptance.Process." + uuid.uuid4().hex
key = uuid.uuid4().hex
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    port = listener.getsockname()[1]
base = f"http://127.0.0.1:{port}"


def request(path, data=None, authenticated=True):
    headers = {"Accept": "application/json, text/event-stream"}
    if authenticated:
        headers["Authorization"] = "Bearer " + key
    if data is not None:
        headers["Content-Type"] = "application/json"
    if path == "/sse":
        headers["MCP-Protocol-Version"] = "2025-11-25"
    with urlopen(Request(base + path, None if data is None else json.dumps(data).encode(), headers), timeout=30) as response:
        body = response.read(512 * 1024 + 1)
        assert len(body) <= 512 * 1024, "Response exceeded smoke budget"
        if "text/event-stream" in response.headers.get("Content-Type", ""):
            messages = [json.loads(line[5:]) for line in body.decode().splitlines() if line.startswith("data:")]
            return next(message for message in messages if "result" in message or "error" in message)
        return json.loads(body)


def rpc(method, parameters):
    result = request("/sse", {"jsonrpc": "2.0", "id": 1, "method": method, "params": parameters})
    assert "error" not in result, result
    return result["result"]


def call(name, **arguments):
    result = rpc("tools/call", {"name": name, "arguments": {"projectContext": project, **arguments}})
    assert not result.get("isError"), result
    return result


with tempfile.TemporaryDirectory(prefix="codemeridian-process-") as directory:
    root = Path(directory)
    repo = root / "repository"
    repo.mkdir()
    (repo / "docs").mkdir()
    (repo / "docs" / "a.md").write_text("# Capability acceptance\n[Second](b.md)\n", encoding="utf-8")
    (repo / "docs" / "b.md").write_text("# Second capability acceptance\n", encoding="utf-8")
    (repo / "stray.md").write_text("# Misplaced capability acceptance\n", encoding="utf-8")
    (repo / "main.ps1").write_text("function Work {}; Work", encoding="utf-8")
    (repo / "policy.json").write_text(json.dumps({"name": "Acceptance", "repositoryConstraints": {
        "contractVersion": "1.0", "groups": [{"id": "docs", "paths": ["**/*.md"]}],
        "placementRules": [{"id": "location", "group": "docs", "allowedPaths": ["docs/**"],
                            "reason": "Documents belong in docs.", "severity": "error"}],
        "referenceRules": [{"id": "links", "sourceGroup": "docs", "allowedTargetGroups": [],
                            "requireLocalResolution": True, "referenceKinds": ["link"],
                            "reason": "Local links must resolve.", "severity": "error"}]}}), encoding="utf-8")
    (repo / "meridian.json").write_text(json.dumps({"version": 2, "project": project,
        "architecture": {"path": "policy.json"},
        "indexing": {"powershell": {"enabled": True, "caseSensitive": os.name != "nt"}}}), encoding="utf-8")
    env = os.environ.copy()
    env.update({"Neo4j__Uri": bolt_uri, "Neo4j__Username": os.environ.get("NEO4J__USERNAME", "neo4j"),
                "Neo4j__Password": os.environ["NEO4J__PASSWORD"], "CodeMeridian_Auth_ApiKey": key,
                "Embedding__Enabled": "true", "Embedding__Provider": "Stub",
                "CODEMERIDIAN_CONFIG_HOME": str(root / "global-config"),
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1"})
    process = None
    log = (root / "server.log").open("w+")

    def stop():
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)

    def start():
        global process
        process = subprocess.Popen(["dotnet", server_dll, "--urls", base], cwd=root, env=env, stdout=log, stderr=log)
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise AssertionError("Server exited during startup")
            try:
                request("/api/v1/project-profiles/?" + urlencode({"projectContext": project}))
                return
            except (URLError, TimeoutError):
                time.sleep(0.2)
        raise AssertionError("Server did not become ready")

    try:
        start()
        profile_path = "/api/v1/project-profiles/?" + urlencode({"projectContext": project, "targetPath": "main.ps1"})
        try:
            request(profile_path, authenticated=False)
            raise AssertionError("Profile endpoint accepted an unauthenticated request")
        except HTTPError as error:
            assert error.code == 401, error.code
        result = subprocess.run([cli, "index", str(repo), "--url", base, "--project", project,
            "--skip-csharp", "--skip-typescript", "--skip-config", "--skip-diagnostics", "--skip-keywords"],
            cwd=repo, env=env, capture_output=True, text=True, encoding="utf-8", timeout=120)
        assert result.returncode == 0, (result.returncode, result.stdout, result.stderr)
        profile = request(profile_path)
        assert profile["status"] == "available" and profile["target"]["kind"] == "powershell", profile
        assert any(item["analyzer"] == "documents" and item["coverage"] == "complete" for item in profile["analyzerOutcomes"])
        assert any(item["coverage"] == "complete" for item in profile["analyzerOutcomes"] if item["analyzer"] == "powershell")
        generation = profile["generation"]
        rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "acceptance", "version": "1"}})
        catalog = rpc("tools/list", {})["tools"]
        assert all(tool.get("annotations") for tool in catalog)
        assert next(tool for tool in catalog if tool["name"] == "get_project_profile")["outputSchema"]
        assert call("get_project_profile", targetPath="main.ps1")["structuredContent"]["generation"] == generation
        exact = call("resolve_exact_symbol", symbol="Work", filePath="main.ps1")
        assert "status: ok" in json.dumps(exact) and "::PowerShellBody::" in json.dumps(exact), exact
        node_id = re.search(r"`([^`]*::PowerShellBody::[^`]*)`", exact["content"][0]["text"])[1]
        context = call("get_context_for_editing", nodeId=node_id)
        assert "unsupported" in json.dumps(context) and "function Work" not in json.dumps(context), context
        assert "status: unknown" in json.dumps(call("resolve_exact_symbol", symbol="Work"))
        constraints = call("find_repository_constraint_violations", findingLimit=1)["structuredContent"]
        if os.name == "nt":
            assert constraints["evaluation"] is not None, constraints
            assert sum(len(rule["findings"]) for rule in constraints["evaluation"]["rules"]) <= 1
        search = call("search_documentation", query="capability", rootPath="docs")
        assert "status: ok" in json.dumps(search), search
        assert "stray.md" not in json.dumps(search), search
        legacy_project = project + ".Legacy"
        legacy_generation = request("/api/v1/project-profiles/begin", {"projectContext": legacy_project})["generation"]
        legacy = {"generation": legacy_generation, "profile": {"contractVersion": "1.0", "project": legacy_project,
            "observedAt": datetime.now(timezone.utc).isoformat(), "discoveryState": "complete", "fileCount": 0,
            "fileKinds": [], "analysisRoots": [], "analysisRootCount": 0, "analyzers": [], "warnings": [], "warningCount": 0}, "files": []}
        request("/api/v1/project-profiles/", legacy)
        assert request("/api/v1/project-profiles/?" + urlencode({"projectContext": legacy_project}))["status"] == "available"
        stop()
        start()
        assert request(profile_path)["generation"] == generation, "Profile did not survive process restart"
        request("/api/v1/project-profiles/begin", {"projectContext": project})
        assert call("get_project_profile", targetPath="main.ps1")["structuredContent"]["status"] == "stale"
        print(json.dumps({"platform": sys.platform, "freshProcess": "passed", "database": "isolated Neo4j", "tools": len(catalog)}))
    except Exception:
        log.flush()
        log.seek(0)
        print(log.read()[-12000:].replace(key, "[acceptance-key]"), file=sys.stderr)
        raise
    finally:
        stop()
        log.close()
