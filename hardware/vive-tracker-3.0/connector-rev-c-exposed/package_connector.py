"""Package only the finished fixed-base revision, with no dependencies or caches."""
from pathlib import Path
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parent


def main():
    parameters = json.loads((ROOT / "parameters.json").read_text())
    revision = parameters["revision"]
    name = f"ELTS_VIVE_Tracker_3_Trigger_Connector_Rev_{revision}"
    report = json.loads((ROOT / "output/verification.json").read_text())
    if not report.get("passed") or report.get("revision") != revision:
        raise RuntimeError("Validate this revision before packaging it.")
    digest = hashlib.sha256((ROOT / "parameters.json").read_bytes()).hexdigest()
    if report.get("parameters_sha256") != digest:
        raise RuntimeError("Dimensions changed after validation; rebuild and validate first.")
    files = [ROOT / filename for filename in (
        "README.md", "BUYING-LIST.md", "parameters.json", "build_connector.py",
        "validate_connector.py", "render_connector.py", "package_connector.py")]
    files += sorted((ROOT / "output").glob("*.step"))
    files += sorted((ROOT / "output").glob("*.stl"))
    files += sorted((ROOT / "output").glob("*.json"))
    files += sorted((ROOT / "previews").glob("*.png"))
    target = ROOT / f"{name}.zip"
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in files:
            archive.write(path, path.relative_to(ROOT))
    with zipfile.ZipFile(target) as archive:
        if archive.testzip() is not None:
            raise RuntimeError("Archive integrity check failed.")
    print(f"{target}\n{len(files)} files; {target.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
