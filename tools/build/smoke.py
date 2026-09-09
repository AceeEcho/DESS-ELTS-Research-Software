"""Run an explicit non-graphical Windows player startup probe."""
import argparse
import json
import os
import subprocess
import tempfile
from pathlib import Path
from tools.build.verify import verify
from tools.build.provenance import sha


def smoke(directory: Path) -> dict:
    if os.name != "nt": raise ValueError("Windows player smoke requires a Windows host")
    root = directory.resolve()
    info = verify(root)
    # Keep diagnostics outside the immutable verified product directory.
    with tempfile.TemporaryDirectory(prefix="elts player smoke ") as temporary:
        log = Path(temporary) / "player.log"
        result = subprocess.run([str(root / "ELTS-Synthetic.exe"), "-batchmode", "-nographics", "-eltsSmokeTest", "-logFile", str(log)],
                                cwd=root, timeout=120, creationflags=subprocess.CREATE_NO_WINDOW, capture_output=True)
        text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
        marker = "ELTS_PLAYER_SMOKE_PASS " + info["version"] + " frames=3"
        if result.returncode != 0 or marker not in text or "ELTS_PLAYER_SMOKE_FAIL" in text:
            failure_log = root.parent / (root.name + "-player-smoke-failure.log")
            failure_log.write_text(text, encoding="utf-8")
            raise ValueError("Player startup probe failed or did not emit its completion marker; exit=" + str(result.returncode) + "; diagnostic=" + str(failure_log))
        return {"result": "pass", "version": info["version"], "playerSha256": sha(root / "ELTS-Synthetic.exe"),
                "marker": marker, "exitCode": result.returncode,
                "limitations": ["Non-graphical startup/configuration and three Update frames only; no GUI, display, hardware or study validation"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    result = smoke(args.directory)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
