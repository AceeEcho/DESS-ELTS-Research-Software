# Local participant collection

The administrator's **Data** button opens a searchable participant viewer. The
Windows player initializes its local folder and `collection.sqlite` at startup;
no SQL server, Python installation or separate database setup is required.

## Saving and identity

Original NDJSON streams remain authoritative and retain their existing logging,
checkpoint and final-summary behavior. The SQL database is a second copy. It is
updated after each test checkpoint, when a recording closes, and when the operator
chooses **Refresh**. Opening Data refreshes the collection too. A partial final
NDJSON line waits for the next refresh; malformed complete lines produce a visible
error. Raw files are never modified by indexing.

A generated profile ID identifies each participant. Participant codes are unique
without ASCII letter case distinctions, so `P-001` and `p-001` belong to one
profile. Use a different code for a different person. Names are optional and may
be updated by a later session using that code. Each new recording and continuation
also saves `participant.json`, so identity survives interruption during startup. Old recordings without participant
metadata use their original folder name as their identifying code.

The viewer groups each session into individual task attempts, including repeats,
stops and skips. Choose an attempt, then **Overview**, **Timeline**, **Shots** or
**Metric guide**. Overview shows firing rates, accuracy, shot precision, aim
stability and measurement coverage. Timeline includes quiet seconds and partial
final seconds. Shots can be filtered by hit, miss or unknown outcome. Both tables
page through 25 rows at a time. Original JSON remains under **Explore original
records**, away from the normal review flow. An open or
incomplete session is labeled accordingly. A final summary indicates a closed
recording, not successful physical validation. Synthetic counts remain synthetic;
scoring and study readiness are unavailable here. Integrity-verified analysis
still uses the retained original recording and existing analysis tools.

## Paths and exports

The actual folder appears below the Data status message. The default is
`collection data` at the clone root. Existing machine overlays retain their
configured relative `machine.dataRoot`; they are not silently rewritten. A player
outside a clone uses its installation directory. The previous default
`data/synthetic` adjacent to the current default folder is also checked for old
recordings. Older nested build directories are not searched recursively.

**Export database** and **Export participant** prepare standalone `.sqlite` files.
Click **Download export** to download the result; another copy remains under
`exports`. A participant export is built in a new empty database with only that
participant's rows; other participants are never copied into that file. A whole
export uses SQLite's backup API for a consistent snapshot, including committed
WAL data. Neither export needs the source database's sidecar files.

**Export Excel** creates a native `.xlsx` workbook for the selected participant,
including all their indexed sessions. Its **Tasks**, **Timeline**, **Shots** and
**Metric guide** sheets use the same calculation as the dashboard and SQL views.
Headers are frozen, filters are enabled, identifiers retain leading zeroes, and
measurement columns are numeric. Missing measurements are blank. Participant text
and notes cannot become formulas. No Excel installation is needed to export.
**Download review CSV** contains the selected session's task summary, with
participant/session identifiers, metric names and units in the column names.

Exports represent the most recent indexed data. Finish a test or choose Refresh
before exporting its latest records. Exporting does not pause a running test.
**Export original files** is available for finalized, inactive sessions and keeps
the original JSON, NDJSON and checksums in a ZIP. Review CSV is a descriptive table,
not a replacement for the raw data or SQL export.

Data is machine-local and ignored by Git. Export files must be backed up separately.
There is no database synchronization, database import, deletion or profile merge
interface in this version.

## SQL layout

| Table | Contents |
| --- | --- |
| `participants` | Unique profile ID, participant code, optional name, creation time |
| `sessions` | Unique session ID, participant relationship, original directory, modification time, finalized flag, descriptive review JSON |
| `records` | Complete original record JSON, keyed by session, stream filename and original byte offset; supplemental JSON uses offset zero |
| `stream_progress` | Last complete imported byte offset for each NDJSON stream |
| `task_results` (view) | One row per task attempt, participant/session identifiers, status, active duration, counts, rates, shot precision, aim metrics and coverage |
| `task_seconds` (view) | One row per active second per task; actual exposure, counts, rates and mean aim error |
| `task_shots` (view) | One row per accepted shot; active time, source timestamp, outcome, reference target, angle and distance |

Each imported line and its progress offset commit together. Imports use bounded
transactions, so retries retain earlier committed batches without duplicating
rows. Foreign keys protect session/profile relationships. `PRAGMA user_version`
is 2. Existing collections gain the metric views on initialization; choose Refresh
to recompute older review JSON from retained events. No raw stream is rewritten.
The Windows runtime uses the system SQLite library with a five-second busy
timeout and full synchronous durability. Raw record size is limited to 16 MB per
line during indexing; the viewer returns 50 records per page.

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
| Aim error variance (deg²) | Sample variance, using n−1, of target-center angular error across valid frame observations. Includes target switching; it is not weapon-position variance. |
| Mean aim speed (deg/s) | Total bore angular travel divided by accepted observation-pair time. |
| Aim speed variability (deg/s) | Duration-weighted population standard deviation of successive angular speeds. A descriptive erraticness proxy; intentional target transitions also affect it. |
| Aim coverage (%) | Accepted observation-pair duration / active duration × 100. Shown alongside valid/total observations and shots with geometry. |

Hits reference the actual hit target. Misses and aim observations reference the
nearest angular visible forward target. This explicit policy does not determine
participant intent. If no candidate exists, target error is unavailable; valid
bore motion can still contribute to speed metrics.

New `ShotFired` payloads retain outcome and geometry before targets are destroyed
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
as **—** in the dashboard, SQL **NULL**, and blank Excel/CSV cells. Older completed
recordings can recover rates and hit outcomes from events, but cannot recover
new geometry that was never captured. Unfinished legacy shots without a hit event
remain **Unknown** until closure; skipped attempts are not zero-performance tests.

## If storage fails

Read the persistent error in Data and the folder shown above it. If startup cannot
create the folder/database, use a writable clone or installation location and
restart ELTS. For SQL-copy errors after a raw save, retain the raw files and choose
Refresh. A malformed complete line or shortened previously imported file needs
inspection; do not edit original files to force an import to pass. SQL errors do
not silently discard raw recordings or mark a synthetic test as physically valid.

Raw writer failures retain their existing session-stop behavior. A secondary SQL
failure is reported separately; its raw checkpoint can still be intact. Linked
recording/export folders and linked files are rejected. Downloads require this
player session's administrator token and an export ID registered by the server;
the browser cannot request arbitrary filesystem paths.

## Software verification

- `tools/runtime-tests/CollectionChecks.csproj`: SQLite creation, incremental
  recovery, malformed-line rollback, Unicode identity, export isolation and the
  real authenticated download server. `TaskDataChecks` also verifies known rates,
  pause exclusion, variance, invalid observations/gaps, partial seconds, legacy
  unavailable values, numeric SQL views and native Excel contents.
- `tools/browser-tests/administrator.cjs`: dashboard controls, profile search,
  exports, raw viewing, persistent errors, blocked browser preference storage,
  keyboard interactions, task selection, timeline pagination, shot filtering,
  Excel downloads and mobile sizing.
- `tools/browser-tests/data-player-smoke.py --build <verified-build-directory>`:
  copies a Windows player into an isolated temporary directory, exercises real
  synthetic recording checkpoints and SQL/ZIP/XLSX downloads, and checks raw/SQL
  row counts using Python's independent SQLite reader.

These are software checks. Another physical machine, hardware measurements and
study acceptance are not established by a local copied-player test.
