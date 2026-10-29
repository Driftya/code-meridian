"""Real CLI/workers on synthetic repositories, using an authenticated HTTP ingestion fixture.

Usage: python capability_matrix_smoke.py /absolute/path/to/CodeMeridian.Indexer[.exe]
Requires preinstalled worker dependencies; never run against user repositories or a live server.
Application routing/query assertions live in ProjectCapabilityMatrixTests.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import shutil
sys.dont_write_bytecode = True
from capability_http_fixture import CapabilityHttpFixture
from inaccessible_directory import inaccessible_directory


cli = Path(sys.argv[1]).resolve(strict=True)
command = [str(cli)]
if cli.suffix == ".dll":
    host = shutil.which("dotnet")
    if host is None:
        raise RuntimeError("A framework-dependent CLI requires dotnet on PATH")
    command = [str(Path(host).resolve(strict=True)), str(cli)]
repository = Path(__file__).resolve().parents[3]
for asset in ("node_modules/.bin/tsx", "node_modules/ts-morph/package.json",
              "node_modules/postcss/package.json", "node_modules/pgsql-parser/package.json"):
    if not (repository / asset).exists():
        raise RuntimeError("Install the Node worker test dependencies explicitly before this smoke: missing " + asset)
cases = [
    ("csharp", {"Main.cs": "class Main { void Run() {} }"}, {}, [], {"csharp": "unknown"}, 0),
    ("typescript-no-config", {"index.ts": "export function run() { return 1; }"}, {}, [], {"typescript": "unknown"}, 0),
    ("markdown", {"chapter.md": "# Chapter\nA synthetic document."}, {}, [], {"documents": "complete"}, 0),
    ("markdown-powershell", {"chapter.md": "# Chapter", "main.ps1": "function Work {}; Work"},
        {"powershell": {"enabled": True, "caseSensitive": os.name != "nt"}}, [],
        {"documents": "complete", "powershell": "complete"}, 0),
    ("sql-disabled", {"schema.sql": "CREATE TABLE things (id integer);"}, {}, [], {}, 0),
    ("sql-enabled", {"schema.sql": "CREATE TABLE things (id integer);"},
        {"sql": {"enabled": True, "defaultDialect": "postgresql", "databaseScope": "fixture"}}, [], {"sql": "complete"}, 0),
    ("mixed-roots", {"api/Main.cs": "class Main {}", "web/index.ts": "export function run() {}",
        "web/tsconfig.json": '{"include":["*.ts"]}', "docs/chapter.md": "# Chapter",
        "web/index.html": '<div class="card"></div>', "web/style.css": ".card { color: red; }"},
        {}, [], {"csharp": "unknown", "typescript": "unknown", "frontend": "unknown", "documents": "complete"}, 0),
    ("unsupported", {"main.py": "raise Exception('must not execute')"}, {}, [], {}, 0),
    ("empty", {}, {}, [], {}, 0),
    ("failed-parse", {"Main.cs": "class {"}, {}, [], {"csharp": "partial"}, 2),
    ("skipped", {"Main.cs": "class Main {}"}, {}, ["--skip-csharp"], {}, 0),
    ("excluded", {"chapter.md": "# Chapter", "node_modules/bad.ps1": "function {"}, {}, [], {"documents": "complete"}, 0),
]
results = []
with tempfile.TemporaryDirectory(prefix="meridian-matrix-å ") as directory:
    base = Path(directory)
    env = os.environ.copy()
    env.update({"CodeMeridian_Auth_ApiKey": "matrix-key", "CODEMERIDIAN_API_KEY": "matrix-key",
                "CODEMERIDIAN_CONFIG_HOME": str(base / "global"), "PSModulePath": str(base / "no-modules")})

    def write(root, path, content):
        target = root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def run(root, fixture, extra=(), expected=0):
        process = subprocess.run([*command, "index", str(root), "--project", "Matrix", "--url", fixture.url,
            "--skip-config", "--skip-diagnostics", "--skip-keywords", *extra], cwd=root, env=env,
            capture_output=True, text=True, encoding="utf-8", timeout=120)
        assert process.returncode == expected, (root.name, process.returncode, process.stdout, process.stderr)
        assert not fixture.errors, fixture.errors
        assert "Restoring npm packages" not in process.stdout, "Worker dependencies must already be available"
        return process

    for name, files, indexing, extra, expected, exit_code in cases:
        root = base / name
        root.mkdir()
        write(root, "meridian.json", json.dumps({"version": 2, "project": "Matrix", "indexing": indexing}))
        for path, content in files.items():
            write(root, path, content)
        with CapabilityHttpFixture() as fixture:
            run(root, fixture, extra, exit_code)
            terminal = [outcome for outcome in fixture.outcomes if outcome["state"] != "running"]
            # Empty document selections may publish explicit successful zero-input coverage.
            actual = {outcome["analyzer"]: outcome["coverage"] for outcome in terminal
                      if outcome["analyzer"] != "documents" or "documents" in expected}
            assert actual == expected, (name, actual, expected)
            if fixture.snapshot:
                inventory = {file["path"]: file["kind"] for file in fixture.snapshot["files"]}
                assert not any(path.startswith("node_modules/") for path in inventory)
                for outcome in fixture.outcomes:
                    assert outcome["sourceFingerprint"] == fixture.snapshot["profile"]["sourceFingerprint"]
                assert not any(outcome["coverage"] == "complete" for outcome in terminal if outcome["analyzer"] not in ("documents", "powershell", "sql"))
                for outcome in terminal:
                    if outcome["analyzer"] == "powershell" and outcome["coverage"] == "complete":
                        assert next(item for item in fixture.snapshot["profile"]["analyzers"] if item["id"] == "powershell")["supportedCapabilities"] == ["powershell_definitions"]
                if name == "sql-disabled":
                    assert inventory["schema.sql"] == "sql"
                    assert not any(path.endswith("/knowledge/sql") for _, path, _ in fixture.requests)
                if name == "typescript-no-config":
                    assert any(item["kind"] == "typescript" and item["path"] == "."
                               for item in fixture.snapshot["profile"]["analysisRoots"])
            results.append({"case": name, "exit": exit_code, "coverage": actual})
            print(json.dumps(results[-1]), flush=True)

    # Persisted cache must survive failed ingestion and changed-byte publication.
    root = base / "recovery"
    root.mkdir()
    write(root, "meridian.json", '{"version":2,"project":"Matrix"}')
    write(root, "chapter.md", "# Before")
    with CapabilityHttpFixture() as fixture:
        run(root, fixture)
        cache = list((root / ".meridian/cache").glob("indexer-files-*.json"))
        assert len(cache) == 1
        cached = cache[0].read_bytes()
        fixture.fail_documents = True
        write(root, "chapter.md", "# Failed transport")
        run(root, fixture, expected=2)
        assert cache[0].read_bytes() == cached
        assert fixture.outcomes[-1]["coverage"] == "partial"
        fixture.fail_documents = False
        write(root, "chapter.md", "# Before mutation")
        fixture.on_document = lambda: write(root, "chapter.md", "# Mutated during upload")
        run(root, fixture, expected=2)
        assert cache[0].read_bytes() == cached
        assert fixture.outcomes[-1]["state"] == "failed"
        assert fixture.generation > fixture.snapshot["generation"], "Stale structural profile was not invalidated"
        results.append({"case": "failed-and-stale-cache-retention", "exit": 2})

    missing = base / "missing"
    with CapabilityHttpFixture() as fixture:
        # A real nonexistent root needs an existing process working directory.
        process = subprocess.run([*command, "index", str(missing), "--project", "Matrix", "--url", fixture.url],
            cwd=base, env=env, capture_output=True, text=True, encoding="utf-8", timeout=30)
        assert process.returncode == 1
        assert not fixture.requests
        results.append({"case": "missing-root", "exit": 1})

    unreadable = base / "inaccessible"
    unreadable.mkdir()
    write(unreadable, "chapter.md", "# Must not become complete empty discovery")
    with CapabilityHttpFixture() as fixture, inaccessible_directory(unreadable):
        process = subprocess.run([*command, "index", str(unreadable), "--project", "Matrix", "--url", fixture.url,
            "--skip-typescript", "--skip-csharp", "--skip-config", "--skip-diagnostics", "--skip-keywords"],
            cwd=base, env=env, capture_output=True, text=True, encoding="utf-8", timeout=30)
        assert process.returncode in (1, 2), (process.returncode, process.stdout, process.stderr)
        assert fixture.snapshot is None, "Inaccessible discovery published a complete inventory"
        assert not fixture.outcomes
        results.append({"case": "inaccessible-root", "exit": process.returncode})

print(json.dumps({"platform": sys.platform, "database": "HTTP fixture", "cases": results}))
