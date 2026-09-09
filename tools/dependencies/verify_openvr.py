"""Offline audit of the pinned OpenVR source/binary bytes; never loads the DLL."""
from __future__ import annotations
import hashlib
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEPENDENCY = ROOT / "unity/Assets/ThirdParty/OpenVR"
REQUIRED_FILES = {"openvr_api.cs", "Plugins/x86_64/openvr_api.dll", "LICENSE", "UPSTREAM-README.md"}


def verify(root: Path = DEPENDENCY) -> dict:
    manifest = json.loads((root / "dependency.json").read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") != 1 or manifest.get("license") != "BSD-3-Clause":
        raise ValueError("Unsupported OpenVR dependency manifest or license")
    records = manifest.get("files", [])
    if len(records) != len(REQUIRED_FILES) or {f["path"] for f in records} != REQUIRED_FILES:
        raise ValueError("OpenVR dependency must contain exactly the reviewed files")
    checksums = []
    for record in records:
        data = (root / record["path"]).read_bytes()
        sha = hashlib.sha256(data).hexdigest()
        blob = hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()
        if sha != record["sha256"] or blob != record["upstreamGitBlobSha1"] or len(data) != record["bytes"]:
            raise ValueError("OpenVR byte/provenance mismatch: " + record["path"])
        expected_url = manifest["origin"].replace("github.com", "raw.githubusercontent.com") + "/" + manifest["upstreamCommit"] + "/" + record["upstreamPath"]
        if record["url"] != expected_url:
            raise ValueError("OpenVR origin URL does not match the pinned commit")
        checksums.append(sha + "  " + record["path"] + "\n")
    if (root / "checksums.sha256").read_text(encoding="utf-8") != "".join(checksums):
        raise ValueError("OpenVR checksum file disagrees with its manifest")
    data = (root / "Plugins/x86_64/openvr_api.dll").read_bytes()
    offset = struct.unpack_from("<I", data, 0x3C)[0]
    if data[:2] != b"MZ" or data[offset:offset+4] != b"PE\0\0" or struct.unpack_from("<H", data, offset+4)[0] != 0x8664:
        raise ValueError("OpenVR native library is not a Windows x86_64 PE image")
    if not (root / "NOTICE").is_file():
        raise ValueError("OpenVR redistribution notice is missing")
    return {"version": manifest["version"], "commit": manifest["upstreamCommit"], "filesVerified": len(records), "nativeArchitecture": "x86_64", "nativeInitialized": False}


if __name__ == "__main__":
    print("PASS: " + json.dumps(verify(), sort_keys=True))
