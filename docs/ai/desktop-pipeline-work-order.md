# Desktop pipeline integration — 2026-09-09

The user authorized a mouse-and-keyboard playable mode through the complete
existing testing pipeline, with interchangeable tracking input for later equipment.
This supplements the accepted DEV software and the administrator dashboard. It does
not reopen accepted DEV items, complete P0.2.S001, or pass physical gates.
Plan and generated state validate at 173 events; G0 remains pending.

Owner: Astra, branch `codex/desktop-input`. Preserve unrelated `elts-simulation/`
changes. Scope: a named Unity entry scene, desktop pose source, acquisition/trigger
integration, playable participant view and administrator handoff, tests, guide and
Windows development build. Positions remain derived from the staged synthetic rig.

Input uses the same paired tracking samples, clock, raw writer, scenario, hitscan,
session timing and replay files as the existing synthetic controller. Gameplay
input must not leak from administrator text fields or survive loss of focus.
Source mode locks while a recording is active. Recorded provenance identifies the
desktop source; sampled poses are not physical measurements.

Acceptance: desktop controls change logged head/weapon poses; valid trigger edges
reference an admitted paired sample and pass through the existing block/scoring
path; all four conditions can run, close and replay/export through existing formats;
recording integrity and reruns remain intact; phase/focus/deadline boundaries reject
unintended shots. Test the new source, relevant existing runtime suites, Unity
end-to-end behavior and a built player probe. Preserve fixed study block durations.

Delegation: Luna owns only the new pure DesktopTrackingSource and its console checks.
Astra owns acquisition integration, shared contracts, gameplay presentation, scene,
final integration and evidence. No worker may alter project progress events.

During verification Luna also investigated acquisition shutdown; Astra integrated
the separate lifecycle lock and ran the final regression suite. See
`desktop-pipeline-verification.md` for the completed software checkpoint.

Native tracking, hardware trigger/ELTS timing, multi-display registration, calibration,
physical E-stop and study readiness remain pending equipment and validation.
