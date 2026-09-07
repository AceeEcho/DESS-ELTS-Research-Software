"""Dependency-free repository policy, documentation and baseline CI entry."""
from __future__ import annotations
import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def policy():
    files = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).decode().split("\0")
    tracked = set(filter(None, files))
    forbidden = ("unity/Library/", "unity/Temp/", "unity/Logs/", "data/", "config/local/", "build/", "release/")
    for name in tracked:
        if name == ".gitmodules" or name == "config/local.json" or name.startswith(forbidden):
            raise ValueError("Forbidden source-control path: " + name)
        if Path(name).suffix.lower() in {".ulf", ".pfx", ".pem"} or Path(name).name == ".env":
            raise ValueError("Credential/deployment file is tracked: " + name)
        if name.startswith("unity/Assets/") and not name.endswith(".meta") and not Path(name).name.startswith("."):
            if name + ".meta" not in tracked:
                raise ValueError("Missing Unity asset metadata: " + name)
        if name.startswith(("scripts/", "tools/", "unity/Assets/ELTS/")) and Path(name).suffix in {".py", ".ps1", ".cs"}:
            if re.search(r"[A-Za-z]:[/\\]Users[/\\]", (ROOT / name).read_text(encoding="utf-8")):
                raise ValueError("Machine-specific user path in executable source: " + name)
    # Accepted originals and immutable evidence retain historical content; they
    # are verified by their hashes, not rewritten to satisfy source-code policy.
    print("PASS: repository paths, asset metadata and executable-source portability")


def documentation():
    # Validate Markdown file links, not illustrative code paths or network URLs.
    files = [ROOT / "README.md", *sorted((ROOT / "docs/ai").glob("*.md")), *sorted((ROOT / "docs/modules").glob("*.md"))]
    pattern = re.compile(r"\[[^\]]+\]\(([^)]+)\)")
    checked = 0
    for file in files:
        if not file.exists(): continue
        content = re.sub(r"```.*?```", "", file.read_text(encoding="utf-8"), flags=re.S)
        for match in pattern.finditer(content):
            target = match.group(1).split("#", 1)[0].strip("<>")
            if not target or re.match(r"[a-zA-Z]+:", target): continue
            if not (file.parent / target).exists():
                raise ValueError(f"Broken documentation link: {file.relative_to(ROOT)} -> {target}")
            checked += 1
    print(f"PASS: {checked} local documentation links")


def run():
    commands = [
        ["tools/plan/build_plan.py", "--check"],
        ["tools/progress/build_schemas.py", "--check"],
        ["tools/progress/validate.py"], ["tools/progress/reduce.py", "--check"],
        ["tools/dependencies/verify_openvr.py"], ["tools/dependencies/verify_unity_packages.py"],
    ]
    for folder in ("plan", "progress", "config", "bootstrap", "dependencies", "build"):
        commands.append(["-m", "unittest", "discover", "-s", "tools/" + folder, "-p", "test_*.py"])
    for args in commands:
        subprocess.run([sys.executable, "-X", "utf8", *args], cwd=ROOT, check=True)


def workflows():
    # JSON is a YAML subset accepted by GitHub; no parser dependency is needed.
    for path in (ROOT / ".github/workflows").glob("*.yml"):
        value = json.loads(path.read_text(encoding="utf-8"))
        if value.get("permissions") != {"contents": "read"}: raise ValueError("CI permission drift")
        for job in value["jobs"].values():
            for step in job["steps"]:
                if "uses" in step and not re.fullmatch(r"[^@]+@[0-9a-f]{40}", step["uses"]):
                    raise ValueError("Action must be pinned to a full commit")
    print("PASS: workflow syntax and action permissions/pins")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", action="store_true")
    args = parser.parse_args()
    policy(); documentation(); workflows()
    if args.baseline: run()
