"""Append one validated event from a reviewable JSON request file.

Example: python tools/progress/event.py --request path/to/request.json
The writer supplies event ID, actual recorded time, task revision, causal hashes
and generated state. Supplied earlier occurredAtUtc preserves truthful journal
provenance; it never backdates event creation.
"""
import argparse
from pathlib import Path
from schema import load_json
from store import append_event


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--request", required=True, type=Path)
    args = parser.parse_args()
    event, state = append_event(Path(__file__).resolve().parents[2], load_json(args.request))
    print(f"Appended {event['eventId']}: {event['target']['id']} -> {event['toStatus']}; "
          f"next {state['execution']['primaryStepId']}")


if __name__ == "__main__":
    main()
