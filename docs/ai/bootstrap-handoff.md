# Verified bootstrap checkpoint

This checkpoint records the BOOT.S026 rehearsal. The current execution selection
always comes from validated `PROJECT_STATE.json`, not from this dated checkpoint.

## Verified result

The accepted plan and preserved source documents have a working event-derived
progress system. The exact Unity 6000.3.23f1 project uses URP and the pinned
Newtonsoft/Test Framework packages, with OpenVR 2.12.14 vendored and XR packages
removed. Editable synthetic configuration is validated and staged from `config/`.

A fresh local clone in a path containing spaces was bootstrapped from an unrelated
working directory. Repeating bootstrap preserved local configuration and staged
content. The corrected clone remained clean through import and tests. Its checks
passed: repository policy/documentation, accepted plan, progress/schema tests,
dependency pins, numerical/configuration/tracking/logging suites, three Edit Mode
tests and one Play Mode test. Two Python configuration tests explicitly skipped
platform-specific cases; these are not claimed as passes.

The first rehearsal exposed a missing terminal newline in logging assembly
metadata. It was corrected, a Unity assembly-boundary regression was added, and
the fresh-clone rehearsal was repeated. Unity's remaining template asset migration
was retained before that final rehearsal.

A clean Windows x64 Mono development build identifies itself as
`dev-27340e5688eb-clean`. Its manifest verifies, and the actual player loaded staged
configuration, rejected study authorization, completed three Update frames and
exited successfully in the explicit non-graphical probe. A copied player directory
also passed manifest and startup verification. This does not verify its visible
GUI or any physical apparatus behavior.

Local development products are under `build/portable development player/`.
They are generated artifacts, not committed source or a study release. Recreate
them with the [build instructions](../modules/windows-build.md).

## Evidence and continuation

`project-management/progress/evidence/BOOT.S026.json` records the tested revisions,
source/artifact hashes and compact results. BOOT.S001-equivalent inventory through
BOOT.S010 retain their actual pre-reducer observations and later truthful import;
BOOT.S011 through BOOT.S025 retain individual immutable receipts. Active IDs are
`P0.3.S001` followed by `BOOT.S002` through `BOOT.S026`; historical workbook suffixes
must not be substituted for them.

The current user-requested stopping point is recorded in
[the 2026-09-08 development handoff](development-checkpoint-2026-09-08.md).
DEV-01 through DEV-12 are accepted in generated progress (173 validated events).
All twelve Trello completion flags are set. The portable package and evidence
are linked in that handoff. Work stops at the user-requested DEV boundary; the
next baseline selection is P0.2.S001, subject to fresh validation on resumption.

The baseline remains anchored at G0. The apparatus is built; testing equipment is
unavailable. Real tracking/occlusion, calibration, display mapping/luminance,
Study-PC full-load throughput, device timing, physical E-stop and shutdown,
exposure behavior and study rehearsal remain pending. Synthetic passes do not
complete mixed baseline tasks or physical gates.

Remote GitHub workflow execution/protection and named research/safety reviewers
remain pending controls under the [documented-ruleset route](../governance/repository-rules.md).
The original bootstrap performed no remote publication, merge, Trello action or
study release. Later DEV completion reconciliation is recorded in the current handoff.
