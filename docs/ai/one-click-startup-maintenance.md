# One-click development startup maintenance

User request (2026-09-09): make START-ELTS open the dashboard and Unity project,
prepare the other computer automatically, and publish the change to main.

This is user-authorized launcher maintenance after the accepted development lane,
not execution of P0.2.S001 or study approval. Plan, progress validator and reducer
checks passed against 173 events, with G0 still pending. No progress events or
physical acceptance claims are added for this out-of-catalog maintenance request.

Scope: START-ELTS.cmd, new scripts/start-dev*.ps1, startup regression tests,
Windows CI coverage, and startup instructions. Preserve pre-existing simulation
edits. Starting revision: 58012eb. Acceptance: visible failures, exact editor pin,
setup before build before interactive launch, repeat-start reuse, and no deletion
of prior builds or data. Existing study startup remains separately fail-closed.

Verification on this Windows computer:

- Seven offline startup orchestration scenarios passed in Windows PowerShell 5.1:
  initial setup/build/launch order, repeat reuse, already-open editor, rebuild
  blocked by open editor, changed-source rebuild, and setup/build failure handling.
- All 38 existing offline setup helper tests passed. New scripts parsed cleanly.
- `tools/ci/check.py --baseline` passed, including documentation and workflow checks.
- The actual startup script completed setup/import, doctor, all available runtime
  suites, Unity EditMode and PlayMode tests, and a Windows dashboard build.
  Local evidence: `diagnostics/setup-report.json`, `diagnostics/test-report.json`,
  `diagnostics/build-report.json`, and `diagnostics/start-build.json`.
- Live processes confirmed the generated ELTS-Synthetic.exe and exact Unity
  6000.3.23f1 Editor were running after startup. This is launch evidence, not a
  complete visual or interactive dashboard acceptance test.
- Running the actual START-ELTS.cmd again exited zero in about four seconds,
  verified/reused the cached player, recognized the already-open Unity project,
  and skipped setup/build. Its transcript is `diagnostics/start-console.log`.

The other computer's installation, account/license state and GUI behavior require
verification on that computer; local tests do not establish those facts.
