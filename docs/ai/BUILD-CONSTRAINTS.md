# Building before testing equipment arrives

The user clarified on 2026-09-06 that the ELTS apparatus is completed and built,
but testing equipment is not currently available. This replaces the earlier
incorrect statement that no physical project parts exist. Build as much software
as possible now; add and configure testing equipment and positions later. The
built ELTS does not establish completion of software integration or test evidence.
Procurement details, physical acceptance, and research/safety decisions remain
subject to their existing requirements.

The user selected **Unity 6000.3.23f1 LTS**. During bootstrap, pin that exact
version in the canonical toolchain manifest and generated/project metadata.
Verify installation, license, launch and builds separately; the previously observed
6000.3.10f1 folder is not the selected project version.

## Configuration and integration

Use the existing configuration architecture. Keep rig geometry and tracker-local
offsets separate from machine-local ports/display assignments and from measured
calibration. Centralize adjustable values; document units, coordinate conventions,
origin and transform direction. Physical placement must be editable without code
changes. Do not introduce a second independently editable configuration source.

Use explicit synthetic configuration and interchangeable tracking/ELTS adapters.
Synthetic examples must be identified and unable to silently qualify as a calibrated
study rig. Unset hardware identity, measurements, and approved safety values remain
unset for study use. Study readiness fails clearly when required inputs are absent.

When parts arrive, bind serial numbers, configure endpoints and display assignment,
measure geometry, calibrate, and execute affected bench procedures. Repositioning
or replacing components requires applicable recalibration and revalidation. A new
rig receives a new identity. Preserve configuration hashes and evidence provenance.

## Evidence and deferred tests

Distinguish unit tests, synthetic integration, target-machine runtime tests, and
physical bench tests. Record source revision (or hashes before Git exists), fixture
and configuration hashes, command/procedure, result, and limitations.

For an unavailable test record task/criterion ID, missing equipment, required later
procedure/evidence, and responsible role. Use existing blocked/verification states
as applicable once the reducer exists. Do not invent progress events before then.
Mixed tasks remain incomplete while any required physical criterion is unmet.

Real tracking and occlusion, physical registration, display mapping and luminance,
mount rigidity, measured calibration, hardware link timing, physical E-stop/light
shutdown, exposure behavior, cross-device rehearsal, and pilots require appropriate
equipment and evidence. Simulator results cannot establish these properties.

## Execution boundary

P0.3.S001 inventory is the agreed initial step. The user accepted
`../plan/PC-001-approved-build-amendment.md`, which resolves conflicting bootstrap
IDs and cyclic dependencies and adds the DEV-01 through DEV-12 development lane.
Those explicit development predicates may permit work before G0, without completing
mixed baseline tasks or passing physical gates. Begin through `START-HERE.md`.

Trello is a coordination mirror. Checklists and automation cannot approve gates or
establish repository progress. Use the accepted amendment and current start guide;
the dated readiness review documents the earlier findings and is historical.
