# Local participant collection

The administrator's **Data** button opens a searchable participant viewer. The
Windows player initializes its local folder and `collection.sqlite` at startup;
no SQL server, Python installation or separate database setup is required.

## Where each kind of data lives

| What | Where | Notes |
| --- | --- | --- |
| Raw recording: tracker samples, targets, events, checksums | One run folder per recording (`samples.ndjson`, `targets.ndjson`, `events.ndjson`, `session-summary.json`) | Authoritative and never modified. Read in place by the viewer, replay and analysis |
| Results: one row per task attempt | `collection.sqlite` (`task_results`) and **Export results** → **Results** sheet | Derived from each recording's events |
| Individual shots | `task_shots` and the **Shots** sheet | One row per accepted shot |
| Participant code | `participant.json` in each run folder, and the collection | Numeric ID; pseudonymous; the only identity in a recording |
| Participant settings | `participant.json` in each new recording | `elts.participant.v4` stores the settings used for that recording in `effectiveSettings`, including source filename and SHA-256, staged configuration digest, applied offsets, screen geometry and screen-match result. |

## Saving and identity

New sessions use a numeric participant code. Existing recordings with older alphanumeric codes remain readable. Each participant start receives a visit ID shared by repeat and continuation recordings. Each new recording stores its code, visit ID and a name-free snapshot of the applied settings in `participant.json`. A legacy recording without an identity file uses its folder name as the code and forms its own visit.

Each checkpoint and successful close updates the local index. Opening Data or choosing **Refresh** reads changed recordings. The index uses input lengths and modification times plus the metric version to recognize previously indexed content. **Rebuild index** under **Export scope and index options** reparses the source files and continues to honor exclusions. **Verify checksums** separately compares the three raw streams and every supplementary file named by a successful closure summary with the writer's SHA-256 values. Use the analysis verifier for complete schema and count checks.

The viewer groups each session into individual task attempts, including repeats,
stops and skips. Choose an attempt, then **Overview**, **Timeline**, **Shots** or
**Metric guide**. Overview shows firing rates, accuracy, shot precision, aim
stability and measurement coverage. Timeline includes quiet seconds and partial
final seconds. Shots can be filtered by hit, miss or unknown outcome. Both tables
page through 25 rows at a time. **Explore original records** pages through the
recording's own files, 50 complete records at a time, away from the normal review
flow. The selector groups recordings by visit and distinguishes active, interrupted and closed recordings. If the writer has closed and the index is still updating, the live status shows **Recording closed · awaiting index**. The last durable checkpoint is shown when one exists. A final summary
indicates a closed recording, not successful physical validation. Synthetic
counts remain synthetic; scoring and study readiness are unavailable here.
Integrity-verified analysis still uses the retained original recording and
existing analysis tools.

On the Overview and Timeline graphs, hover or drag to inspect a time interval.
The cursor highlights that interval and shows its **hit, shot and miss counts**,
their **per-second rates**, and **mean aim error**. The time slider supports touch,
Left/Right arrows and Home/End; horizontal scrolling or Shift + scroll steps
through intervals. Ordinary vertical scrolling continues to scroll the page.
The inspector uses the saved bins, including partial final seconds; it does not
interpolate measurements or turn unavailable values into zero. Its position stays
in place across live updates and tab changes within the same attempt.

Live polling includes a collection revision and status. The Data view fetches the authenticated historical snapshot only when visible and the revision changes. Raw paging caches byte offsets for the current stream. Live polling does not rebuild unchanged task controls or graphs. Parsed reviews
and rendered markup are cached, and attempt/tab navigation stays in place when
changing sections so held clicks and keyboard focus are preserved.

## Paths and exports

The actual folder appears below the Data status message. The default is
`collection data` at the clone root. Existing machine overlays retain their
configured relative `machine.dataRoot`; they are not silently rewritten. A player
outside a clone uses its installation directory.

**Export results** defaults to the whole collection. Under **Export scope and index options**, choose one participant, one visit, or a range of UTC recording modification dates. **Preview row counts** reports matching results, shots and timeline rows. The workbook streams its sheets from a single read transaction.

- **Results**, **Shots** and **Timeline** retain their existing typed columns and definitions. Empty measurements stay blank, and identifiers preserve leading zeroes.
- **Metric guide** describes each metric and unit.
- **Recordings** identifies the source runs, visit and session IDs, closure status, checksum status and check time, export filters, modification times and metric version. It also gives the standard-interval analysis status and reason for each recording and its complete visit.
- **Handoff** provides a row per attempt for linking external heart-rate, eye-tracking and questionnaire records. Add references to a working copy after collection. External file fields start empty and clock alignment is marked **Unmapped**.
- **Raw archive** packages one closed recording. **Recovery copy** exports an interrupted recording with a `recovery-` filename and indicates the missing closure summary. The active writer must finish before either can copy its files. Use **Download export** for the generated ZIP or workbook.

Results represent the most recent indexed save. Finish a test or choose Refresh
before exporting its latest records. Exporting does not pause a running test.

Data is machine-local and ignored by Git. Export files must be backed up separately.
There is no database synchronization, database import or profile merge
interface in this version.

## Removing collected data

Select a participant and choose **Remove participant data** to remove their profile
and all indexed runs, or select a saved session and choose **Remove
selected run** to remove that whole recording. Confirm the removal in the browser;
Cancel leaves everything intact. A run here is one recording session, including its
task attempts. Removing the last run also removes the empty participant profile.

Removal commits in one transaction and refuses any selection that includes the
currently open recording. Finish or abort that session first. The viewer clears
removed data and invalidates its current download link.

Original recording files and previously created exports remain on disk. The database remembers the excluded recording directories so Refresh
and restart cannot import them again. A new recording using the same participant
code can still be collected normally.

For a full reset, choose **Settings → Remove all participant data…**, type
`CONFIRM` exactly, then click **Remove**. This removes the station's original
recordings (including excluded and interrupted runs), reservations, SQLite
index, managed exports, participant calibrations and calibration drafts. The
player invalidates open calibration tabs and reopens an empty index. Room
configuration and copies stored outside the station are retained. A full reset
requires an idle player and exclusive access to both data stores. If an I/O
error interrupts removal, close the app holding the files and repeat the reset.

## SQL layout

| Table | Contents |
| --- | --- |
| `participants` | Unique profile ID, participant code, creation time |
| `sessions` | Unique session ID, participant relationship, original directory, modification time, finalized flag, descriptive review JSON |
| `removed_recordings` | Excluded original directories that Refresh must not import again |
| `task_results` (view) | One row per task attempt, participant/session identifiers, status, active duration, counts, rates, shot precision, aim metrics and coverage |
| `task_seconds` (view) | One row per active second per task; actual exposure, counts, rates and mean aim error |
| `task_shots` (view) | One row per accepted shot; active time, source timestamp, outcome, reference target, angle and distance |
| `recording_analysis`, `visit_analysis` (views) | Standard 300-second interval screening status and reason per recording and visit |

The Data viewer, Results sheet and review CSV explain known exclusions for each attempt. Screening uses the event markers and the recording's closure/checksum status. An attempt may be skipped, aborted, interrupted, paused, use flexible timing, lack a start or end marker, or have a duration other than 300 seconds. A record with **Needs analysis** requires the [offline analysis reader](analysis.md), which checks counts, schemas and timing against the raw files. An integrity check marked **Passed** confirms the compared bytes only. A visit with some excluded recordings and others requiring analysis is marked **Partially excluded**.

Each import commits in one transaction. Foreign keys protect session/profile
relationships. Schema version 6 introduced the source index stamp and removed the legacy name column. Version 7 adds recording, visit and attempt analysis screening. Migration preserves identifiers and exclusions, runs once in a transaction and refuses databases from newer versions. Opening a version 3 or earlier
collection drops its copy of raw lines (`records`, `stream_progress`) and
reclaims the space; choose Refresh to recompute review JSON from retained events.
No raw stream is rewritten. The Windows runtime uses the system SQLite library
with a five-second busy timeout and full synchronous durability. Raw record size
is limited to 16 MB per line in the records viewer.

## Measure dashboard polling with a large selected review

On Windows, after the Unity package cache is available, run this optional loopback benchmark from the repository root. It generates 1,000 fictional attempts in memory and prints JSON. The two runs publish the same 30 live updates at 10 Hz with ten commands and one change to the historical index. Compare response-body bytes, producer-thread managed allocations for JSON serialization and UTF-8 publication, and command acknowledgment and observation times. The fixture uses a small common live envelope, so its payload sizes describe the cost attributable to the historical review. The Unity player's frame and memory measurements are separate.

```powershell
$jsonDll = (Get-Item 'unity/Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll' | Select-Object -First 1).FullName
dotnet run --project tools/runtime-tests/CollectionChecks.csproj -c Release "-p:NewtonsoftPath=$jsonDll" -- --benchmark-review
```

## Metric definitions and missing data

Calculations are versioned as `elts.task-metrics.v1` in `TaskDataReview`. These
are descriptive synthetic observations; they are not the primary study scoring
path or integrity-verified analysis.

| Measure | Definition |
| --- | --- |
| Hits/s, shots/s, misses/s | Accepted count divided by active seconds, excluding pauses. Each timeline bin divides by its own exposure, including a fractional final bin. |
| Accuracy (%) | 100 × hit shots / accepted shots. Trigger lockout and invalid-tracking clicks are excluded from accepted shots. No shots means unavailable accuracy. |
| Shot error (degrees) | Angle between the corrected bore ray and target-center direction at the trigger observation. P95 uses nearest rank. |
| Shot offset / miss distance (mm) | Perpendicular distance from reference target center to the forward bore ray. Miss distance averages only misses with geometry. This is not a screen-plane impact coordinate. |
| Edge clearance (mm) | max(0, center offset − target radius). |
| Mean aim error (deg), **official** | `meanAimErrorDeg`: mean target-center angular error over valid frame observations with a visible forward target, pauses excluded. This is ELTS's official aim error. The analysis tool's `rawSampleMeanAimErrorDegrees` is a 250 Hz cross-check with a different target policy, not a substitute. |
| Aim error variance (deg²) | Sample variance, using n−1, of target-center angular error across valid frame observations. Includes target switching; it is not weapon-position variance. |
| Mean aim speed (deg/s) | Total bore angular travel divided by accepted observation-pair time. |
| Aim speed variability (deg/s) | Duration-weighted population standard deviation of successive angular speeds. A descriptive erraticness proxy; intentional target transitions also affect it. |
| Aim coverage (%) | Accepted observation-pair duration / active duration × 100. Shown alongside valid/total observations and shots with geometry. |

Hits reference the actual hit target. Misses and aim observations reference the
nearest angular visible forward target. This explicit policy does not determine
participant intent. If no candidate exists, target error is unavailable; valid
bore motion can still contribute to speed metrics.

`ShotFired` payloads retain outcome and geometry before targets are destroyed
or replaced. `AimObserved` events use the current unpredicted paired pose and the
visible target state on the Unity frame. The default observation interval is
0.05 s (at most 20 Hz), with a maximum accepted motion gap of 0.25 s. Configure
`runtime.aimObservationIntervalSeconds` and `runtime.aimMaximumGapSeconds`; these
values are recorded with observations. Actual frame cadence may be slower than
the configured rate. The full raw tracker stream remains at its configured
acquisition cadence. Aim telemetry uses the existing critical event writer;
a recording failure retains the existing stop behavior.

Motion calculations do not connect across pauses, invalid poses, duplicate or
backward observation timestamps, or excessive gaps. Missing measurements display
as **—** in the dashboard, SQL **NULL**, and blank Excel cells. A shot without a
recorded outcome stays **Unknown** and is never counted as a miss; skipped
attempts are not zero-performance tests.

## If storage fails

Read the persistent error in Data and the folder shown above it. If startup cannot
create the folder/database, use a writable clone or installation location and
restart ELTS. For collection errors after a raw save, retain the raw files and
choose Refresh. A malformed complete event line needs inspection; do not edit
original files to force an import to pass. Collection errors do not silently
discard raw recordings or mark a synthetic test as physically valid.

Raw writer failures retain their existing session-stop behavior. A secondary
collection failure is reported separately; its raw checkpoint can still be intact.
Linked recording/export folders and linked files are rejected. Downloads require
this player session's administrator token and an export ID registered by the
server; the browser cannot request arbitrary filesystem paths.

## Software verification

- `tools/runtime-tests/CollectionChecks.csproj`: SQLite creation, upgrade of older
  databases, unchanged and changed refreshes, source checksums, in-place record paging, intake profiles, exclusions and authenticated collection access. `TaskDataChecks` also
  verifies known rates, pause exclusion, variance, invalid observations/gaps,
  partial seconds, unknown outcomes, numeric SQL views and the all-participant
  results workbook without names.
- `tools/browser-tests/administrator.cjs`: dashboard controls, profile search,
  exports, raw viewing, persistent errors, blocked browser preference storage,
  keyboard interactions, task selection, timeline pagination, shot filtering,
  Excel downloads and mobile sizing. Regression checks hold pointer presses
  across live polls, assert unchanged task DOM/review caching, and inspect graph
  counts, rates, missing values and partial intervals with pointer and keyboard.
- `tools/browser-tests/data-player-smoke.py --build <verified-build-directory>`:
  copies a Windows player into an isolated temporary directory, exercises real
  synthetic recording checkpoints and XLSX/ZIP downloads, and checks with
  Python's independent SQLite reader that no raw lines or names are duplicated.

These are software checks. Another physical machine, hardware measurements and
study acceptance are not established by a local copied-player test.
