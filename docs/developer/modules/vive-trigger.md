# VIVE gun trigger input

The calibration workspace reads the selected **Weapon** tracker's trigger and
shows **pressed**, **released**, or **unavailable** above the 3D view, with observed
press/release counts. It never substitutes the head tracker. Button availability
is independent of optical pose validity; stale input is unavailable. Counts are
diagnostic observations, not shot counts or switch-timing measurements.

In SteamVR **Manage Trackers**, assign the gun tracker **Held in hand**, then
**Right Hand** or **Left Hand**. The applications only read this configuration;
they do not change roles. Assigning a SteamVR role can change pose conventions,
so review the calibration workspace's mounting/registration after a role change.

For the main app, set `weaponTriggerSerial` in `config/local.json` to the exact
gun tracker serial. An empty or absent value disables the physical trigger unless
`START-ELTS.cmd` finds a weapon serial in the latest saved calibration profile.
An explicit machine serial takes precedence over that profile. Restart/rebuild
with `START-ELTS.cmd` after changing the binding. No device index is persisted and
no other tracker is used when the selected one disconnects.

Each new pull uses the existing participant firing path: tutorial practice,
the unscored "shoot to begin" action, or a scored shot in a running test. The
physical trigger fires on the **rising edge** (press); mouse controls retain their
existing release behavior. A held trigger never repeats. After reconnecting,
pausing, or changing phases, release before pulling again. Existing ammunition,
trigger lockout, recorded-pose admission, and scenario-state checks still apply.

`ViveTriggerState` records changes observed by the app, with availability, raw
button mask, packet number, serial, and the shared host clock. Accepted input
events carry `source=vive-tracker-trigger-v1`, `edge=rising`, the tracker serial,
and the recorded pose acquisition sequence. Mouse/automatic events use `none`
for their inapplicable tracker serial. Polling is once per Unity update, so very
short pulses between frames can be missed; timestamps are not hardware latency
measurements. The calibration bridge samples independently at 60 Hz.

This change supplies physical **button input**. The main app's aiming/pose source
remains its existing desktop/synthetic source, and recordings retain that
provenance. It does not add live weapon-pose aiming or apparatus output. Automated
Unity batch runs disable hardware reads so regression tests use scripted input.

Verification: `dotnet run --project tools/runtime-tests/TriggerChecks.csproj`,
the calibration Python/browser tests, and Unity PlayMode tests cover holds,
reconnects, phase changes, source provenance, recorded poses, and shot dispatch.
