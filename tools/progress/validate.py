"""Validate schemas, immutable catalog/exports, events and generated state."""
from pathlib import Path
from build_schemas import contracts
from import_plan import export_outputs
from schema import validate_file
from store import generate


def main():
    root = Path(__file__).resolve().parents[2]
    export_outputs(root, check=True)
    state = generate(root)
    folder = root / "project-management/progress/handoffs"
    if folder.exists():
        from schema import load_json
        for path in folder.glob("*.json"):
            validate_file(load_json(path), root / "schemas/progress/handoff.schema.json")
    print(f"PASS: catalog/exports/events/state/handoffs; next {state['execution']['primaryStepId']}")


if __name__ == "__main__":
    main()
