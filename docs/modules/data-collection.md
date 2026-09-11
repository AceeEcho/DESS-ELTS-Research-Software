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

The viewer shows descriptive counts, notes and original JSON records. An open or
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

Each imported line and its progress offset commit together. Imports use bounded
transactions, so retries retain earlier committed batches without duplicating
rows. Foreign keys protect session/profile relationships. `PRAGMA user_version`
is 1. The Windows runtime uses the system SQLite library with a five-second busy
timeout and full synchronous durability. Raw record size is limited to 16 MB per
line during indexing; the viewer returns 50 records per page.

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
  real authenticated download server.
- `tools/browser-tests/administrator.cjs`: dashboard controls, profile search,
  exports, raw viewing, persistent errors, blocked browser preference storage,
  keyboard interactions and mobile sizing.
- `tools/browser-tests/data-player-smoke.py --build <verified-build-directory>`:
  copies a Windows player into an isolated temporary directory, exercises real
  synthetic recording checkpoints and SQL/ZIP downloads, and checks raw/SQL row
  counts using Python's independent SQLite reader.

These are software checks. Another physical machine, hardware measurements and
study acceptance are not established by a local copied-player test.
