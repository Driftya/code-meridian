"""Exercise a published self-contained worker without installed dotnet/pwsh.

Usage: python packaged_smoke.py /absolute/path/to/CodeMeridian.PowerShellIndexer[.exe]
Only synthetic sources under a temporary directory are created or analyzed.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


worker = Path(sys.argv[1]).resolve(strict=True)
with tempfile.TemporaryDirectory(prefix="meridian-powershell-package-") as directory:
    root = Path(directory)
    marker = root / "executed.txt"
    effect = "[System.IO.File]::WriteAllText('" + str(marker).replace("'", "''") + "', 'executed')"
    env = os.environ.copy()
    env.update({"PATH": "", "DOTNET_ROOT": str(root / "no-runtime"), "DOTNET_MULTILEVEL_LOOKUP": "0",
                "HOME": str(root), "USERPROFILE": str(root), "XDG_CONFIG_HOME": str(root / "config"),
                "PSModulePath": str(root / "modules")})

    def write(name, text, encoding="utf-8"):
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding=encoding)
        return path

    for profile in ("Documents/PowerShell/profile.ps1", "Documents/WindowsPowerShell/profile.ps1", "config/powershell/profile.ps1"):
        write(profile, effect)
    write("modules/Poison/Poison.psm1", effect + "; function Poison {}")
    write("helper.ps1", "function Help {}")
    write("main.ps1", '. "$PSScriptRoot/helper.ps1"; Help')
    write("unicode.ps1", "function Get-É { 'こんにちは' }; Get-É", "utf-16")
    write("safe-using.ps1", "using module Poison\n" + effect)
    write("assembly.ps1", "using assembly './never-load.dll'\n" + effect)
    write("manifest.psd1", "@{ RootModule='modules/Poison/Poison.psm1'; Note=(" + effect + ") }")
    write("dsc.ps1", '"$(configuration Unsafe { Node localhost {} })"')

    def run(arguments, expected):
        process = subprocess.run([str(worker), *arguments], cwd=root, env=env, text=True,
                                 encoding="utf-8", capture_output=True, timeout=30)
        assert process.returncode == expected, (process.returncode, process.stderr)
        assert not marker.exists(), "Source, module, manifest, or profile side effects executed"
        return json.loads(process.stdout) if process.stdout.strip() else None

    identity = run(["--describe-worker"], 0)
    assert identity["packageVersion"] == "7.6.8"
    assert identity["parseMode"] == "module_analysis_syntax_only"
    assert identity["parserFingerprint"].startswith("sha256:")

    def analyze(names, expected):
        request = write("request.json", json.dumps({"project": "PackageFixture", "rootPath": str(root),
                                                   "caseSensitive": os.name != "nt", "files": names}))
        return run(["analyze", str(request)], expected)

    report = analyze(["helper.ps1", "main.ps1", "unicode.ps1"], 0)
    assert report["coverage"] == "unknown", "A parser smoke must not activate generic capabilities"
    assert all(command["bindingStatus"] == "resolved" for file in report["files"] for command in file["commands"])
    report = analyze(["safe-using.ps1", "manifest.psd1", "dsc.ps1", "assembly.ps1"], 2)
    by_path = {file["path"]: file for file in report["files"]}
    assert not any(item["code"] == "ModuleNotFoundDuringParse" for item in by_path["safe-using.ps1"]["diagnostics"]), "Public parser module resolution was not bypassed"
    assert by_path["manifest.psd1"]["manifest"]["status"] == "indeterminate"
    assert by_path["dsc.ps1"]["diagnostics"][0]["code"] == "configuration_unsupported"
    if os.name != "nt":
        (root / "linked.ps1").symlink_to(root / "helper.ps1")
        report = analyze(["linked.ps1"], 2)
        assert report["files"][0]["status"] == "failed"
        assert not report["files"][0]["bodies"]
    oversized_request = write("oversized.json", " " * 1048577)
    assert run(["analyze", str(oversized_request)], 1) is None
    unsupported_request = write("unsupported.json", json.dumps({"contractVersion": "2.0", "project": "PackageFixture",
                                                              "rootPath": str(root), "caseSensitive": True, "files": []}))
    assert run(["analyze", str(unsupported_request)], 1) is None
    print(json.dumps({"platform": sys.platform, "package": identity["packageVersion"],
                      "parseMode": identity["parseMode"], "smoke": "passed", "symlinkBoundary": os.name != "nt"}))
