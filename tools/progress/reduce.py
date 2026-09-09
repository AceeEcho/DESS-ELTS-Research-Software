"""Validate or regenerate PROJECT_STATE.json from the accepted event store."""
import argparse
from pathlib import Path
from store import generate


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Atomically replace stale generated state")
    parser.add_argument("--check", action="store_true", help="Check committed state without changing it (default)")
    args = parser.parse_args()
    state = generate(Path(__file__).resolve().parents[2], write=args.write)
    print(f"PASS: {state['generatedFrom']['eventCount']} events; baseline {state['current']['gateId']}; "
          f"execution {state['execution']['mode']} {state['execution']['primaryStepId']}")


if __name__ == "__main__":
    main()
