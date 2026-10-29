"""Actual producer receipts and root membership on synthetic repositories only.

Usage: python producer_audit_smoke.py <built-cli.dll>
Uses authenticated synthetic ingestion; isolated database publication has separate fixtures.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
from capability_http_fixture import CapabilityHttpFixture

cli = Path(sys.argv[1]).resolve(strict=True)
command = ["dotnet", str(cli)] if cli.suffix == ".dll" else [str(cli)]
cases = [
    ("sql", {"a.sql": "CREATE TABLE app.t(id integer);"}, "sql", 0, "complete"),
    ("sql-partial", {"a.sql": "SELECT * FROM unqualified;"}, "sql", 2, "partial"),
    ("sql-parse", {"a.sql": "SELECT FROM !"}, "sql", 1, "unknown"),
    ("typescript-diagnostic", {"a.ts": "export function work() { return missingName; }"}, "typescript", 2, "partial"),
    ("frontend-parse", {"style.css": ".card {", "page.html": '<div class="card"></div>'}, "frontend", 2, "partial"),
    ("configuration-parse", {"appsettings.json": "{ invalid"}, "configuration", 2, "partial"),
    ("configuration", {"appsettings.json": '{"Enabled":true}'}, "configuration", 0, "unknown"),
    ("nested-roots", {"web/tsconfig.json": '{"include":["*.ts"]}', "web/a.ts": "export function a() {}",
        "web/nested/tsconfig.json": '{"include":["*.ts"]}', "web/nested/b.ts": "export function b() {}",
        "loose.ts": "export function loose() {}", "web/style.css": ".card { color:red; }",
        "loose.css": ".loose { color:blue; }"}, "typescript", 0, "unknown"),
]

with tempfile.TemporaryDirectory(prefix="producer-audit-") as directory:
    base = Path(directory)
    env = os.environ.copy()
    env.update({"CodeMeridian_Auth_ApiKey": "matrix-key", "CODEMERIDIAN_CONFIG_HOME": str(base / "global")})
    for name, files, analyzer, exit_code, coverage in cases:
        root = base / name
        root.mkdir()
        config = {"version": 2, "project": "Matrix"}
        if analyzer == "sql":
            config["indexing"] = {"sql": {"enabled": True, "defaultDialect": "postgresql", "databaseScope": "audit"}}
        (root / "meridian.json").write_text(json.dumps(config), encoding="utf-8")
        for relative, text in files.items():
            file = root / relative
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(text, encoding="utf-8")
        with CapabilityHttpFixture() as fixture:
            arguments = [*command, "index", str(root), "--project", "Matrix", "--url", fixture.url,
                         "--skip-csharp", "--skip-docs", "--skip-diagnostics", "--skip-keywords"]
            if analyzer != "configuration":
                arguments.append("--skip-config")
            if analyzer not in ("typescript", "frontend"):
                arguments.append("--skip-typescript")
            result = subprocess.run(arguments, cwd=root, env=env, capture_output=True, text=True, encoding="utf-8", timeout=120)
            assert result.returncode == exit_code, (name, result.returncode, result.stdout, result.stderr, fixture.errors)
            terminal = [item for item in fixture.outcomes if item["analyzer"] == analyzer and item["state"] != "running"]
            assert terminal and all(item["coverage"] == coverage for item in terminal), (name, terminal)
            assert not fixture.errors, fixture.errors
            if exit_code:
                assert not list((root / ".meridian/cache").glob("indexer-files-*.json")), "Incomplete producer advanced cache"
            if name == "nested-roots":
                assert {item["scope"] for item in terminal} == {".", "web", "web/nested"}, terminal
                nodes = [node for method, path, body in fixture.requests if method == "POST" and path.endswith("/nodes/bulk") for node in body]
                file_paths = [node["filePath"] for node in nodes if node.get("type") == "File" and node.get("sourceHash")]
                expected = [path for path in files if path.endswith((".ts", ".css"))]
                assert sorted(file_paths) == sorted(expected), file_paths
            print(json.dumps({"case": name, "coverage": coverage, "exit": exit_code}), flush=True)
