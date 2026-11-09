# Pupil timing integration follow-up

Recorded November 2, 2026. Planning note for **D-12** (cross-device time
synchronization), the P2 shared clock, P6 endpoint timing, and **P8.4**
(cross-device synchronization rehearsal). Implementation and physical timing
validation remain pending.

## Reference to account for

The supplied [Pupil Time Sync Protocol specification](https://github.com/pupil-labs/pupil/blob/0fbccd412a9e0ff553eb91727dd0da54d33e9637/pupil_src/shared_modules/time_sync_spec.md)
is **v1, draft**, pinned to commit
`0fbccd412a9e0ff553eb91727dd0da54d33e9637`. Treat it as an integration
reference; first confirm the actual Pupil product, software version, enabled
plugins, and supported synchronization interface used by the collaborators.

The draft describes ZRE discovery in a named synchronization group, ranked
master selection and reselection, and a TCP clock service responding to `sync`.
Wire timestamps are little-endian 64-bit floating-point seconds. Followers
periodically collect 60 request/response observations, discard the slowest 30%,
and estimate offset and variance from the remainder. These are reference
protocol details, not measured ELTS performance or approved timing thresholds.

## Required planning work

1. **Resolve D-12 with the collaborators.** Decide whether to use the deployed
   Pupil synchronization interface directly or an explicitly documented clock
   mapping. Identify the clock authority and how Unity, Pupil, Jetson, HRV, and
   IMU recordings relate to it. Do not assume the other devices support Pupil's
   protocol. Define any shared recorded marker or physical synchronization signal.
2. **Preserve the ELTS clock and raw evidence.** Keep
   `SharedMonotonicClock` as the process timestamp authority. Pupil seconds are
   not ELTS 100 ns ticks or UTC. Plan an explicit mapping that retains original
   source timestamps, local receive times, clock identity, offset estimates,
   round-trip observations, uncertainty, and mapping validity intervals. Preserve
   original recordings; store alignment results separately with provenance.
   Account for drift, resynchronization, clock resets, master changes, and reconnects
   without stepping the ELTS acquisition clock or silently reordering observations.
3. **Specify and check the adapter before integration.** Confirm offset sign and
   midpoint calculation against the deployed implementation, using known offsets
   in both directions. Cover units, byte order, partial TCP reads, malformed or
   delayed responses, stale estimates, discovery scope, and master loss/change.
   Keep deployment settings and approved quality limits configurable. Synthetic
   tests can check these software behaviors while equipment is unavailable.
4. **Make alignment quality reviewable.** Define acceptable residual error,
   drift, estimate age, and behavior when synchronization is unavailable with
   the research lead. Record uncertainty and flag unsupported alignment claims.
   Unity's UTC startup anchor and provisional mock offset fields alone do not
   establish alignment between devices or actual stimulus onset.
5. **Include this in P8.4 acceptance.** Rehearse with the actual collaborator
   devices and selected software versions. Retain shared markers near session
   start and end and across test blocks, measure residual alignment and drift,
   exercise reconnect/resynchronization, and verify that each collaborator can
   identify every block boundary. Record the method, configuration, evidence,
   limitations, and responsible reviewers' acceptance.

The apparatus is built, but testing equipment is unavailable. This note does
not approve D-12, complete P8.4 or any gate, or validate physical timing. Trello
completion flags do not replace the approved method and measured evidence.
See [required follow-up](hardware-follow-up.md), [tracking](../developer/modules/tracking.md),
[logging](../developer/modules/logging.md), and the [software safety boundaries](../safety/README.md).
