"""Validate exact required Unity package versions against manifest and lock."""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.progress.schema import load_json, validate_file


def verify(root=ROOT):
    config = load_json(root / "config/unity-packages.json")
    validate_file(config, root / "schemas/build/unity-packages.schema.json")
    required = config["required"]
    if len({item["id"] for item in required}) != 3:
        raise ValueError("Required package identities must be distinct")
    manifest = load_json(root / "unity/Packages/manifest.json")["dependencies"]
    locked = load_json(root / "unity/Packages/packages-lock.json")["dependencies"]
    for item in required:
        name, version = item["id"], item["version"]
        if manifest.get(name) != version or locked.get(name, {}).get("version") != version:
            raise ValueError("Required Unity package pin mismatch: " + name)
        if locked[name].get("source") != item["source"] or locked[name].get("depth") != 0:
            raise ValueError("Required Unity package must have its reviewed direct source: " + name)
    for names in (manifest, locked):
        if any(n.startswith("com.unity.xr.") or n in {"com.unity.modules.xr", "com.unity.modules.vr"} for n in names):
            raise ValueError("XR packages are forbidden")
    project_version = (root / "unity/ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    if "m_EditorVersion: " + config["unityVersion"] + "\n" not in project_version:
        raise ValueError("Unity package config and project Editor version disagree")
    return {item["id"]: item["version"] for item in required}


if __name__ == "__main__": print("PASS: " + json.dumps(verify(), sort_keys=True))
