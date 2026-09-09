"""Print compact event-derived status, active ownership and open work."""
from pathlib import Path
from store import generate


def main():
    state = generate(Path(__file__).resolve().parents[2])
    print(f"Baseline: {state['current']['phaseId']} / {state['current']['gateId']}")
    print(f"Execution: {state['execution']['mode']} / {state['execution']['primaryStepId']}")
    done = [key for key, record in state["atomicSteps"].items() if record["status"] == "done"]
    print(f"Verified atomic steps: {len(done)} / {len(state['atomicSteps'])}")
    print("Next eligible: " + ", ".join(state["execution"]["nextEligibleStepIds"]))
    for work in state["activeWork"]:
        print(f"Owner: {work['owner']} / {work['taskId']} / {work['branch']}")
    for key, record in state["atomicSteps"].items():
        for blocker in record["blockers"]:
            print(f"Blocked {key}: {blocker['id']}: {blocker['requiredAction']}")
    for warning in state["integrity"]["warnings"]:
        print(warning)


if __name__ == "__main__":
    main()
