"""Run user-facing suites locally, or select affected suites from a CI diff."""
import argparse
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SUITES = {
    "analysis": (("analysis/", "schemas/", "tools/logging/"), [
        [sys.executable, "-m", "unittest", "discover", "-s", "analysis/tests"]]),
    "calibration": (("Calibration Environment/", "tools/config/", "unity/Assets/ELTS/Config/"), [
        [sys.executable, "-m", "unittest", "discover", "-s", "Calibration Environment/tests"],
        ["node", "--test", *[p.as_posix() for p in sorted((ROOT / "Calibration Environment/tests").glob("*.test.mjs"))]]]),
    "browser": (("tools/browser-tests/", "tools/runtime-tests/fixtures/", "unity/Assets/StreamingAssets/administrator/", "unity/Assets/ELTS/Operator/"), [
        ["node", "tools/browser-tests/administrator.cjs"]]),
    "demo": (("elts-simulation/",), [["node", "elts-simulation/build.mjs", "--check"]]),
    "narration": (("docs/participant-tutorial/", "tools/build/export_participant_tutorial.py", "unity/Assets/ELTS/"), [
        [sys.executable, "tools/build/export_participant_tutorial.py", "--check"]]),
}


def selected(base):
    if not base or not base.strip("0"):
        return set(SUITES)
    changes = subprocess.run(["git", "diff", "--name-only", "-z", base, "HEAD"], cwd=ROOT,
                             stdout=subprocess.PIPE, check=False)
    if changes.returncode:
        return set(SUITES)
    files = changes.stdout.decode("utf-8").split("\0")
    if any(path.startswith((".github/workflows/", "tools/ci/")) for path in files):
        return set(SUITES)
    return {name for name, (paths, _) in SUITES.items() if any(path.startswith(paths) for path in files)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--select", action="store_true", help="Publish the browser dependency decision to GitHub Actions")
    args = parser.parse_args()
    chosen = selected(os.environ.get("DIFF_BASE"))
    if args.select:
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            print("browser=" + str("browser" in chosen).lower(), file=output)
    else:
        for name, (_, commands) in SUITES.items():
            print(("RUN " if name in chosen else "SKIP ") + name, flush=True)
            if name in chosen:
                for command in commands:
                    subprocess.run(command, cwd=ROOT, check=True)
