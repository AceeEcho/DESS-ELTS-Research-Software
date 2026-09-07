# Developer quick start

This is a development setup for software connected to the already built ELTS.
Testing equipment is currently unavailable. No synthetic test establishes physical
calibration, safety, timing, display, or study-readiness acceptance.

1. Copy or clone the repository into a writable path; spaces are supported.
2. Provide Python 3.10+ and the exact Unity version declared in the root README.
3. Run the five read-only verification commands in [README.md](../../README.md).
4. Read [the current state](../../PROJECT_STATE.json) and the exact active step in
   [the approved plan](../plan/approved-plan.json). Use `execution.primaryStepId`.
5. For implementation, follow [START-HERE.md](../ai/START-HERE.md). Do not hand-edit
   generated state or append an event until its dependencies, claims, and evidence
   validate. The writer rejects competing task branches and stale state.

If Python is not available, install a supported interpreter or invoke an existing
supported interpreter by its full executable path. Do not assume a Codex-specific
runtime path exists on another machine. Portable bootstrap/build scripts are added
and verified by their own steps; consult state before relying on them.

A schema or state mismatch is a stop condition for dependent work. Preserve the
error and reconcile the source/events; do not delete history or make the generated
state match an expected status manually. A missing device blocks its physical
checks while eligible synthetic work can continue under PC-001.
