"""Verify packaging refuses apphost-only and missing-parser/runtime staging without building."""
from pathlib import Path
import subprocess
import tempfile


project = Path(__file__).resolve().parents[1] / "CodeMeridian.Indexer.csproj"
common = ["CodeMeridian.PowerShellIndexer.dll", "CodeMeridian.PowerShellIndexer.deps.json",
    "CodeMeridian.PowerShellIndexer.runtimeconfig.json", "System.Management.Automation.dll", "System.Private.CoreLib.dll"]
platforms = {
    "win-x64": ["CodeMeridian.PowerShellIndexer.exe", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll"],
    "linux-x64": ["CodeMeridian.PowerShellIndexer", "libhostfxr.so", "libhostpolicy.so", "libcoreclr.so"],
}
with tempfile.TemporaryDirectory(prefix="meridian-worker-packaging-") as directory:
    root = Path(directory)
    files = [root / rid / file for rid, native in platforms.items() for file in common + native]
    for file in files:
        file.parent.mkdir(parents=True, exist_ok=True)
        file.touch()

    def run():
        return subprocess.run(["dotnet", "msbuild", str(project), "-nologo",
            "-target:RequirePowerShellWorkersForPackage", f"-property:PowerShellWorkerArtifactsRoot={root}"],
            capture_output=True, text=True, timeout=30)

    assert run().returncode == 0
    for file in files:
        file.unlink()
        result = run()
        assert result.returncode != 0, f"Packaging accepted missing {file}"
        assert file.name in result.stdout, result.stdout + result.stderr
        file.touch()
    print(f"Packaging gate passed: complete staging and {len(files)} missing-file cases")
