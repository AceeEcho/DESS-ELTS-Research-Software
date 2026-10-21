# Session troubleshooting

Use this when a study session stalls and you need the next check, not a tour of the software. It covers the Unity participant window, the browser administrator dashboard, local export, install and startup, the Python analysis tools, and the simulated Jetson controller. The hardware section covers only what this repository establishes about the trigger, Vive trackers, the SteamVR base station, a wearable eye tracker, a heart-rate monitor, and the ELTS apparatus.

Written 21 October 2026. The study hardware had arrived that day, and this repository contains no physical-validation record. Repository text still says testing equipment is unavailable. A passing setup, doctor report, or synthetic session does not accept a device, a calibration, or a study gate. See [required follow-up](hardware-follow-up.md) and [safety boundaries](../safety/README.md).

Claims below are taken from the current source, configs, schemas, and docs. Where a model, cable, driver, pairing step, port, or procedure is absent, the gap is marked and named in [the gap register](#gap-register).

## How to read a failure

1. Leave the error window open. Setup and startup print the failed step, a **How to resolve** note, and the log path. A PowerShell exit code is the wrapper; the Unity or Python text under it is the failure.
2. Copy the named log before retrying. Later runs replace `diagnostics/start-console.log`, `diagnostics/setup-console.log`, and `diagnostics/*-report.json`. Each participant launch keeps its own `diagnostics/player-start-*.log`.
3. Keep the recording folder. Indexing, export, and analysis read it. They do not rewrite `samples.ndjson`, `events.ndjson`, `targets.ndjson`, or `session-summary.json`.
4. Treat a green software check as a software check. `diagnostics/doctor-report.json` always includes the limitation `Development diagnostics only; no physical, safety or gate acceptance.`

Commands assume the repository root on Windows x64, which is the only platform the player, `SETUP-DEV.cmd`, and `START-ELTS.cmd` target. Python tools also run anywhere the pinned interpreter range is available. From another directory, call the scripts by full path; they locate the repo from their own path.

## Quick symptom index

| What you see | Go to |
| --- | --- |
| Setup or **START-ELTS.cmd** stops, Unity license **198**, compiler error, timeout, or "another Unity instance" | [Install and startup](#install-and-startup) |
| Participant window closes, or the log contains `ELTS_VIEW_INIT_FAIL` | [Participant view failed to initialize](#participant-view-failed-to-initialize) |
| Dashboard says **Offline** | [Dashboard says Offline](#dashboard-says-offline) |
| **ARM TEST** stays disabled, or the test never leaves "Shoot to begin" | [Arming and the participant trigger](#arming-and-the-participant-trigger) |
| Click does nothing, magazine stuck, or the pose freezes after you leave the game window | [Mouse and keyboard participant](#mouse-and-keyboard-participant) |
| **SAVE FAILED**, Data shows an error, export will not download | [Local data and export](#local-data-and-export) |
| `INGEST FAILED` or `scoreStatus: unavailable` | [Python analysis](#python-analysis) |
| Lights, Jetson, or E-stop do not respond | [Simulated controller](#simulated-jetson-controller) and [ELTS apparatus and Jetson](#elts-apparatus-and-jetson) |
| Trackers, base station, SteamVR, eye tracker, or heart rate never appear | [Hardware](#hardware) — expect this on the current build |
| Targets move in a way you did not configure, or there is no hit flash or shot sound | [Open decisions](#open-investigator-decisions) |

## What the running session actually is

`START-ELTS.cmd` builds or reuses `ELTS-Synthetic.exe`, then opens two windows: the participant game and a loopback browser dashboard. The browser path forces the input source **Mouse and keyboard** (`DevelopmentStationSession` sets `DesktopInputOption`). Poses come from `DesktopTrackingSource` with identity `desktop-mouse-keyboard-room-v1`. The staged tracker serials are the literals `SYNTHETIC-HEAD` and `SYNTHETIC-WEAPON` in [`config/rig/templates/synthetic-rig.json`](../../config/rig/templates/synthetic-rig.json). The station then marks displays, SteamVR, and both trackers ready in code (`SteamVrRunning=true`, `TrackersBoundAndValidForTwoSeconds=true` in `DevelopmentSessionPanel`). The workbench label states the substitution: `Emulated: displays and SteamVR. In-process mock controller; no device connection.`

The same startup always constructs `MockEltsLink` around `SimulatedEltsController`. That object has an in-memory `LedPermission` flag. [`jetson/`](../../jetson/README.md) has no listener, serial adapter, network adapter, GPIO, or systemd unit. Configuration accepts only `mock://elts` or `null://elts` (`config/local.example.json`, `schemas/config/local.schema.json`). Any other endpoint fails configuration load with `physical output is not enabled in development`. The desktop station does not read `EltsEndpoint` when it creates the link, so `null://elts` still runs the mock. `NullEltsLink` exists for the link checks; the player does not select it.

`config/staging.json` allows only `"mode": "synthetic"`. `StudyReady` is forced false. `FIRST-RUN.cmd` and `scripts/start-study.ps1` call the driver action `start-study`, which fails closed with: `Study provisioning/packaging/startup is unavailable: approved study configuration, physical acceptance and release gates remain pending.`

The offline page [`elts-simulation/index.html`](../../elts-simulation/README.md) draws a briefing illustration. It does not open a session or a device. Its README lists the illustration's limits; those limits are repeated as gaps below.

## Install and startup

Operator walkthrough: [setup on more than one computer](multi-machine-setup.md#if-something-goes-wrong). Tool pins: [`config/toolchain.json`](../../config/toolchain.json) and [toolchain notes](toolchain.md). This section is the command and file map behind that guide.

### Files and commands

| Item | Where it is |
| --- | --- |
| One-time setup | `SETUP-DEV.cmd` → `scripts/setup-dev.ps1` |
| Session launch | `START-ELTS.cmd` → `scripts/start-dev.ps1` → `scripts/start-dev-tools.ps1` |
| Diagnostics only | `CHECK-SYSTEM.cmd` → `scripts/doctor.ps1` → `python -X utf8 tools/bootstrap/driver.py --action doctor` |
| Study entry, fails closed | `FIRST-RUN.cmd`, `scripts/start-study.ps1` |
| Tool paths for this machine | `%LOCALAPPDATA%\ELTS\Tools\` and ignored `config/local/development-tools.json` |
| Machine overlay | ignored `config/local.json`, copied from `config/local.example.json` on first bootstrap if missing |
| Setup result | `diagnostics/setup-report.json` (`result` must be `ready` and `toolchainSha256` must match `config/toolchain.json`), plus `diagnostics/setup-console.log` and `diagnostics/setup-unity-import.log` |
| Startup transcript | `diagnostics/start-console.log` |
| Startup lock | `diagnostics/start.lock` |
| Build cache | `diagnostics/start-build.json` (`fingerprint`, `output` matching `build/start-elts-<32 hex chars>`) |
| Player | `build/start-elts-<id>/ELTS-Synthetic.exe` |
| This launch's Unity log | `diagnostics/player-start-<guid>.log` (path printed as `Player log:`) |
| Build log when compiling | `build/start-elts-<id>/unity-build.log`, report `diagnostics/build-report.json` |
| Doctor report | `diagnostics/doctor-report.json` and `diagnostics/doctor-*.log` |
| Editor version | Unity **6000.3.23f1** (`09d2ecc7fb28`), `unity/ProjectSettings/ProjectVersion.txt` |
| Python | 3.10 up to, and not including, 3.15 (`config/toolchain.json`). Windows setup installs 3.13.15. Tested 3.12.14. |
| .NET SDK for console checks | `10.0.400`, `global.json` with roll-forward disabled. The player does not need this SDK. |
| Node.js | 24.21.0, used to rebuild `elts-simulation/`, not to run a session |

`START-ELTS.cmd` launches the player with:

```text
-eltsWindowed -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile <diagnostics/player-start-*.log>
```

It waits up to 120 seconds for a line matching `ELTS_ADMINISTRATOR_READY http://127.0.0.1:<port>/#`. The startup window closes after that line. Errors stay up, with **Open startup log**.

Doctor, from the repo root:

```powershell
.\CHECK-SYSTEM.cmd
```

A custom interpreter or Editor:

```powershell
.\scripts\doctor.ps1 -PythonExecutable C:\path\python.exe -UnityEditor C:\path\Unity.exe
```

Doctor checks Python against the toolchain range, records the Unity Editor as pass or deferred, runs `tools/dependencies/verify_openvr.py` and `tools/dependencies/verify_unity_packages.py`, and runs configuration staging in `--check` mode. A missing Editor is then a hard failure: `Install the exact selected Unity Editor through Unity Hub, then rerun`. Package verification rejects any `com.unity.xr.*` dependency and `com.unity.modules.xr` / `com.unity.modules.vr` (`config/toolchain.json` `forbiddenPackagePrefixes`). The OpenVR audit hashes the vendored SDK and does not load the DLL.

Portable software checks, with Python 3.10+:

```powershell
python tools/ci/check.py --baseline
```

`scripts/verify.ps1` runs doctor plus the available suites. A deferred Unity result is reported as deferred. See [CI](../modules/ci.md).

### Startup lock or "another Unity instance"

**Likely cause.** `diagnostics/start.lock` is held, or `unity/Temp/UnityLockfile` is held by an Editor or a setup process (`Test-EltsProjectOpen` in `scripts/start-dev-tools.ps1`).

**Check.** Read the message. If it says the startup lock could not be acquired, another `START-ELTS` window is still in setup or build. The script tells you to leave `diagnostics/start.lock` in place while that process runs.

**Fix.** Let the other window finish, or close the Unity Editor that has this `unity/` project open, then run `START-ELTS.cmd` again. Setup and rebuild both refuse to continue while the project is open: `Close this project in Unity, then click START-ELTS again`.

### Unity exit 198, wrong Editor, or checksum mismatch

**Likely cause.** The messages and fixes already collected in [multi-machine setup](multi-machine-setup.md#if-something-goes-wrong) match the installer: license exit **198**, `runtime-config` path-separator failures on older trees, `error CS…` in the Unity log, verification assertions in `diagnostics/test-report.json`, download checksum mismatch (the installer is not executed; the message names `diagnostics/setup-console.log`), and timeouts. The PowerShell launcher leaves a timed-out install running so it does not kill an installer.

**Check.** `diagnostics/setup-unity-import.log` for licensing. `unity/ProjectSettings/ProjectVersion.txt` must contain `m_EditorVersion: 6000.3.23f1`. Doctor fails with `Unity ProjectVersion.txt disagrees with toolchain manifest` when it does not.

**Fix.** Use that guide's row for the exact text. Keep `config/local.json` and the recording folders. Rerun `SETUP-DEV.cmd` after the license or source fix. The Store alias `WindowsApps\python.exe` is skipped by `scripts/common.ps1`; set `-PythonExecutable` or `ELTS_PYTHON` to a real interpreter if discovery throws `Provide a supported Python executable`.

### Saved build will not open

**Likely cause.** `diagnostics/start-build.json` is stale, the fingerprint of `unity/Assets`, `unity/Packages`, `unity/ProjectSettings`, `config`, `tools/build`, `tools/bootstrap`, and `scripts` changed, or `python -m tools.build.verify` rejected the folder. A warning then says the saved build could not be verified and a new `build/start-elts-*` directory is used. Old build folders are left in place.

**Check.** `diagnostics/start-console.log` and, after a compile, `build/start-elts-*/unity-build.log`. Empty output directory is required for `scripts/build.ps1 -Output <new-empty-dir>` ([windows build](../modules/windows-build.md)).

**Fix.** Close the Editor and run `START-ELTS.cmd` again. If the executable is missing after a build, the launcher throws `Build finished without a dashboard executable.` Send that build log with the report; keep recordings out of the report you share.

### Participant view failed to initialize

**Symptom.** Startup says the game closed before the dashboard was ready and prints the exit code and `Player log:`. Or the log line is `ELTS_VIEW_INIT_FAIL ...` and startup says `The participant view could not initialize`.

**Likely cause.** `DevelopmentView.Awake` loads `StreamingAssets/config-generated` and calls `DevelopmentGameSettings.Validate`. Failure is logged as `ELTS_VIEW_INIT_FAIL` plus the exception, and the on-screen box is `Development view unavailable: ` plus that message. The known scene-settings text is `Development game settings are outside their safe synthetic ranges.` (active targets 1–30, spawn positions 31–50 with more positions than targets, path length 100000–1000000, layout scale 0.95–1.15, move speed 1.5–4.5 m/s, magazine 1–100). Configuration load also fails closed on a non-synthetic bundle, a bad hash, or an endpoint other than `mock://elts` / `null://elts`.

**Check.** The `diagnostics/player-start-*.log` from this attempt, not an older log. The startup script ignores a player log that does not contain a fresh ready or fail line.

**Fix.** For the safe-range message, update and rebuild with `START-ELTS.cmd`; keep `config/local.json` and recordings ([setup guide](multi-machine-setup.md#if-something-goes-wrong)). For a configuration error, fix the named field and restage:

```powershell
python tools/config/stage.py --check
python tools/config/stage.py --write
```

Staging rules are in [configuration staging](../modules/config-staging.md). `dataRoot` must be a relative path with no drive letter, `..`, or Windows reserved device name. Display indices must differ and be in 0–31. Those indices are stored on `MachineConfiguration` and are not read by the player when it places windows.

If the process is still running after the 120-second wait, the launcher leaves it up: `The participant game did not initialize its dashboard within 120 seconds.` Close that window, read the player log, then retry.

## Participant application

Scene: `unity/Assets/Scenes/ELTSDesktop.unity`. Product name `ELTS-Synthetic`, company `ELTS Research`, bundle `0.1.0` (`unity/ProjectSettings/ProjectSettings.asset`). Editor play of that scene is the manual path in [desktop input](../modules/desktop-input.md). `OPEN-DESKTOP-TEST.cmd` expects a build in `build/desktop-pipeline`. The one-click launcher uses the `build/start-elts-*` player instead, and it does not open the Editor.

### Two windows and where the picture comes from

The participant camera renders the synthetic library. The dashboard's outside view is a JPEG of the operator camera (`previewWidth` default 1920, height 16:10, JPEG quality 90, default 10 fps). It is not a camera-device feed. The user guide says the camera feed is not connected in the screenshots. The simulation's "central Brio camera" is an illustration; see [GAP-HW-ELTS-LIGHTS](#gap-register).

Standalone players set `vSyncCount` to 0 and `targetFrameRate` from `participantFramesPerSecond` (default 60, serialized range 15–120). That cap is presentation only. The recording clock is `SharedMonotonicClock`. `Application.runInBackground` is true, so switching to the dashboard does not pause the session clock. `DevelopmentSessionPanel` counts frames whose `unscaledDeltaTime` exceeds 1/30 s and would label them `Frames over 33 ms`. The browser station hides that workbench and does not refresh the label, so the count is not on the dashboard. It is a diagnostic, not a pass/fail limit.

Fullscreen: F11 or Alt+Enter. Escape or F1 calls `SetFullscreen(false)` on the separate player (`ReturnToAdministrator`). Settings can also change windowed and fullscreen. `-eltsWindowed` on the launcher overrides `-eltsSecondaryDisplay` and `-eltsFullscreen`.

**Moving the game to another screen.** The launcher does not apply `participantDisplayIndex` or `operatorDisplayIndex`. [The dashboard reference](../modules/operator-dashboard.md) says to connect a wireless TV with Win+K before launch, then move the window with Win+Shift+Left/Right in extended desktop. ELTS does not keep a Miracast link up. A disconnected wireless display has to be reconnected in Windows.

> **GAP-HW-DISPLAY.** The repo does not record which physical panel or projector is the study screen, its resolution, or a measured corner map. `synthetic-rig.json` marks geometry `synthetic_unmeasured`. The display rectangle is 1.2 m wide and 0.675 m tall at z = 1.5 m. `screenBottomHeightAboveFloorM` is `1.2192` (the staging doc calls this four feet) and is an assumption, not a surveyed floor. Who can answer: the rig and calibration operator, and the tracking/display engineer, in [hardware follow-up](hardware-follow-up.md).

### Mouse and keyboard participant

This is the input the browser station records. It is a stand-in for a later `ITrackingSource` and a device trigger adapter ([desktop input](../modules/desktop-input.md)).

| Input | Effect |
| --- | --- |
| Mouse inside the game image | Aims on the fitted screen. The reticle is hidden until the block is running. |
| Left press, then release, inside the image | One round on release. The press that starts an armed test is the start click. |
| W / S, A / D, Q / E | Virtual eye forward/back, left/right, down/up. Speed 0.35 m/s. Initial distance 2 m. |
| R | Reload. Magazine capacity default 20. No automatic reload. |
| Home | Reset the virtual pose. |
| Focus loss | Cancels a held click. The last pose is published again on later frames (`StationPublishPose` still runs). |

A shot is refused when the pointer is outside the image, the block is not armed or running, or the click was held across an arm/disarm or a resize (`GeometryChangedEvent` cancels the click). Empty-magazine clicks are dry fires, not shots. Head, body, and limb damage are 3, 2, and 1 against 3 health ([scenario](../modules/scenario.md)).

`DesktopTrackingSource` marks a sample invalid (`Disconnected` / `Unavailable`, null pose) when nothing has been published or the last publish is older than `MaximumSampleAgeSeconds` (0.25 s). Invalid samples are logged as invalid. Analysis drops them from aim error and does not interpolate them.

The acquisition thread is named `ELTS synthetic acquisition` even for mouse input. Configured cadence is `sampleRateHz` **250** in [`config/defaults/runtime.json`](../../config/defaults/runtime.json). Open **Readiness & diagnostics** on the dashboard. **Synthetic tracking** shows `Fresh paired observations; <n> Hz observed.` or `Waiting for fresh virtual poses; keep the participant app open.` A lower number means the thread is not completing polls at that cadence. It is not a tracker radio reading. **Recording** shows `Writer active; dropped samples: <n>` or `Writer failed. Retain this recording and start a new attempt.` Sample queue capacity is **8192**. A full sample queue increments `DroppedSampleCount`. Event queue capacity is **1024**. If the critical queue cannot accept a record, the writer goes unhealthy and the session must stop ([logging](../modules/logging.md)).

Trigger lockout is `triggerLockoutSeconds` **0.02**. Aim observations are at most every `aimObservationIntervalSeconds` **0.05** (20 Hz), and gaps above `aimMaximumGapSeconds` **0.25** break the aim-speed series. Those values are written with the observations.

### Arming and the participant trigger

**ARM TEST disabled.** The dashboard enables it only when state `canArm` is true. Finish calibration (**Generate fixture**, review, **Accept fixture**), practice, and the break, and wait until the test save finishes. Order edits lock while a participant session is open. Gray controls are inactive on purpose ([user guide](user-guide.md)).

**Stuck on "Shoot to begin".** That title is shown while `TestArmed` is true (`participantTitle`). The participant must click and release inside the game image. The dashboard cannot synthesize that click (`DevelopmentTestStation` says no browser action can manufacture a participant trigger). **DISARM TEST** returns to ready.

**Practice shots are not scored.** Practice is a timer from the staged session (`practiceDurationSeconds` **10** in `config/defaults/session.json`) or from the dashboard practice field. **Finish practice** and **Restart practice** are administrator actions.

**The next test does not start by itself.** After a save, **Start break** begins the countdown (`breakDurationSeconds` **5** in the staged session file; the dashboard field overrides it for this station, 0–3600 s). When the break ends, arm the next test yourself.

**Pause does not stop the clock source.** Pause holds the test timer, target motion, lifetimes, and respawn delay. Acquisition and logging continue. The simulated WE link is stopped on pause and prepared again with the remaining duration on resume. Switching windows is not a pause.

### Participant view shows a synthetic room, not the lab

The library, mannequins, and outside preview are the development scene ([rendering](../modules/rendering.md)). The operator camera's IMGUI banner includes the fixed text `Base station not connected`. That string is drawn by `DevelopmentView`; it is not a poll of hardware. Head-dropout and weapon-dropout toggles on the workbench are software simulations. Missing head pose hides the screen feed. Missing weapon pose hides the bore marker. The body silhouette is inferred from the head pose.

There is no hit flash, shot sound, or score overlay driven by `ScenarioDefinition.ShowHitVisual`, `PlayShotSound`, or `ShowScore`. Those flags default to false and nothing in the player reads them. The HUD still draws ammo, hits, and shots. See [D-06](#open-investigator-decisions).

### Calibration fixture is not a rig calibration

**Generate fixture** builds a deterministic synthetic fixture (240 poses, virtual corner means, virtual zero hold). **Accept fixture** writes a sidecar beside the recording with create-new semantics and an operator note containing the filename and SHA-256. It does not replace `config/rig` or the staged effective config. Accepted records have `mode: synthetic` and `studyReady: false`. The usual verifier does not automatically check that sidecar ([calibration](../modules/calibration.md)). Thresholds are development limits, not D-17 approval criteria.

> **GAP-DEC-D17.** D-17 is named only as the approval those thresholds are not. The repo does not record the numeric acceptance limits or the capture procedure. Who can answer: the calibration operator and the research lead ([hardware follow-up](hardware-follow-up.md), pivot/corners/eye/weapon-zero row).

## Browser administrator dashboard

Reference: [administrator dashboard](../modules/operator-dashboard.md). Assets served from `unity/Assets/StreamingAssets/administrator/`: `index.html`, `styles.css`, `app.js`, `workflows.js`, `data-viewer.js`. Any other path returns `Not found`.

### Address, token, and port

`AdministratorServer` listens on `127.0.0.1` only. Preferred port is **18764** (`PreferredPort`). If `Start` throws `SocketException`, it binds port **0** and uses whatever port the OS assigned. The origin is `http://127.0.0.1:<port>`. The browser URL is that origin plus `/#<token>`. The token is a new GUID each process. A discovery file `elts-administrator.json` (`url`, `pid`) is written under Unity's `Application.temporaryCachePath`. This repo does not hardcode that Windows folder.

Startup opens Microsoft Edge at `%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe` if that file exists, otherwise Chrome at `%ProgramFiles%\Google\Chrome\Application\chrome.exe`, with `--app="<url>" --window-size=1280,900 --new-window`. If neither file exists, it opens `server.Url` with the system handler. `-eltsNoAdminWindow`, `-runTests`, and `-eltsSmokeTest` skip the browser. The intro (`TexasTechIntro`) finishes before the browser is started.

API calls send `X-Elts-Token`. The host must equal `127.0.0.1:<port>` or the response is 403 with an empty body. A bad token on `/api/` returns 403 and `Open the administrator link from this Unity session.` Commands are `POST /api/command` with `Origin` equal to the server origin, body at most **16384** bytes. More than **8** queued commands returns 429 `Please wait for the current action.` A command not started within **1.2 s** returns `Action expired before Unity received it. Please try again.` The server accepts at most **12** concurrent connections and drops the rest. State is `GET /api/state`. The preview is `GET /api/view.jpg` (HTTP 204 when no JPEG has been published).

The page polls `/api/state` about every `1/previewFps` seconds while visible (preview fps clamped to 5–30, default 10) and about once a second while `document.hidden`.

Workspace layout for the embedded Unity dashboard is `operator-workspace-v1.json` under `Application.persistentDataPath` ([dashboard reference](../modules/operator-dashboard.md)). Browser presets and pane width use `localStorage` keys such as `elts-admin-pane`. If the browser blocks storage, controls still run; the page comment says preferences become session-local.

### Dashboard says Offline

**Likely cause.** `app.js` sets **Offline** when `fetch` of `/api/state` throws or returns a non-OK status. The phase text becomes `Unavailable` and the arm, participant, note, and workflow buttons are disabled.

**Check.**

1. The participant window is still running. The server is inside that process.
2. The address is the URL from this launch (`ELTS_ADMINISTRATOR_READY` in `diagnostics/player-start-*.log`), including the hash token. An old bookmark targets a dead port or token.
3. You are on `127.0.0.1`, not another host name. A different host fails the host check.
4. Only one player is bound. If 18764 was taken, this launch is on another port; the log line is the authority.

**Fix.** Close extra player processes, start once with `START-ELTS.cmd`, and use the window it opens. Refreshing the page is enough after a brief stall if the player is still up. Reloading does not create another recording.

### Command rejected

**Likely cause.** The JSON result `ok: false` carries `exception.Message` from `ApplyCommand`. Common text:

| Message | Meaning |
| --- | --- |
| `Finish the current participant first.` | A session is still open. |
| `Use 1–40 letters, numbers, hyphens or underscores for the participant ID.` | ID must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$`. |
| `Name is limited to 120 characters and initial notes to 2000 characters.` | Intake limits. |
| `The participant changed. Review the current session and try again.` | Command `runId` does not match the open station, and the action is not a data action. |
| `Order must be a permutation of the four conditions.` | `SessionPlan` requires each of `WE_MT`, `NE_MT`, `WE_FT`, and `NE_FT` once. |
| `At least 2 GB free disk space is required.` | Free space on the drive of the resolved data root is under 2 GiB (`MinimumDiskBytes`, and the same text in `SessionReadinessReport`). |
| `Cannot create recording in <path>: ...` | `LoggingRunDirectory.ReserveUnique` failed. The run id must be 1–80 ASCII letters, digits, hyphen, or underscore. |
| `Synthetic session setup failed.` | The engine was aborted in the setup `catch` after it had been created. The rethrown exception message is what the command returns; also read the player log. |
| `Data is still loading. Please wait.` | A collection action is still in flight. |
| `Finish the participant session before exporting the raw recording.` / `Finish this recording before exporting its original files.` | Export of originals waits for a closed session. |
| `This recording is incomplete. Retain it for recovery.` | No `session-summary.json`. |
| `Confirm removal before deleting data.` | Removal requires the browser confirm step. |

**Fix.** Use the message's path and the Data view error. Keep the raw folder. See [local data](#local-data-and-export).

### Durations, order, and presets

Test durations are seconds from **1** to **3600**, default **300** (`MinimumDevelopmentDurationSeconds` / `MaximumDevelopmentDurationSeconds`). They save on blur for the station and reset when the process exits. A new duration must be longer than active time already played. Practice and break use **Save practice & breaks**. Presets live in this browser only. The staged file `config/defaults/session.json` still has practice 10 s, break 5 s, and order `WE_MT`, `NE_MT`, `WE_FT`, `NE_FT` until you restage; the dashboard fields override the running station without rewriting that file.

Condition codes must be each of `WE_MT`, `NE_MT`, `WE_FT`, and `NE_FT` once. `SessionPlan` rejects anything else.

## Local data and export

Reference: [local participant collection](../modules/data-collection.md) and [logging](../modules/logging.md).

### Where files go

`MachineConfiguration.ResolveDataRoot` walks parents of the player install until it finds both `config/toolchain.json` and `unity/ProjectSettings/ProjectVersion.txt`, then appends `dataRoot`. A player copied outside a clone uses the install directory plus `dataRoot`. The default `dataRoot` is `collection data` (a folder name with a space) at the clone root. An existing `config/local.json` keeps its own relative `dataRoot`; staging will not rewrite it. Editor recordings resolve from `unity/Assets` upward to that same root.

The Data view prints the folder it is using. **Open folder** starts `explorer.exe` on that directory. Git ignores `/collection data/`, `/data/`, `/diagnostics/`, and `/config/local.json`. Pushing the repo does not back up recordings.

Beside the root, the indexer also looks at a sibling `data/synthetic` (the previous default). It does not recurse into nested build folders. It skips reparse points (directory links).

On startup the player creates the folder and `collection.sqlite` even before the first participant. Windows uses the system library `winsqlite3` (editor/standalone Win). Busy timeout is **5000** ms. `PRAGMA user_version` is 3. A raw line over **16 MB** fails indexing with `Raw record exceeds 16 MB.` The viewer pages **50** records. `Cannot open collection database: ` plus the SQLite error means the file could not be opened.

Each run directory can contain:

| File | Role |
| --- | --- |
| `samples.ndjson` | Paired head and weapon samples, schema `elts.samples.v1` |
| `targets.ndjson` | `elts.targets.v1` or `elts.targets.v2` for the whole file, never mixed |
| `events.ndjson` | `elts.events.v1` |
| `session-summary.json` | Closure. Readers require `complete: true` and `error: null` |
| `.session-summary.pending.json` | In-progress closure. Readers ignore it |
| `test-checkpoint-<attempt>.json` | Per-test durable checkpoint, `finalized: false` |
| `participant.json` | Identity, written so a startup interruption still has the code |
| `.<runId>.reservation` | Reservation file beside the run, under the data root |

Run ids that collide gain `-r0001`, `-r0002`, and so on. Flush interval is `logFlushSeconds` **1**. The dashboard shows **Saved** only after both logging queues pass the checkpoint and the checkpoint file is published. `SAVE FAILED:` plus the exception aborts the session. The raw files already written stay on disk. A later SQL failure is a second message: `SQL copy needs retry: `. **Refresh** imports again. `Data action failed. Raw recordings are retained. Retry with Refresh.` is the collection-action wrapper.

The run folder name is the hidden workbench field. The browser station sets it to the typed id, a hyphen, UTC `yyyyMMdd-HHmmss`, a hyphen, and six hex characters (`DevelopmentStationSession`). `participant.json` in that folder stores the typed code as `participantId` (`elts.participant.v1`). Import prefers that file, then a `Participant details:` note, then the folder name. Participant codes match without ASCII case: `P-001` and `p-001` are one profile. Names are optional.

### Export buttons

| Control | Result |
| --- | --- |
| **Export Excel** | `.xlsx` for the selected participant. Sheets Tasks, Timeline, Shots, Metric guide. No Excel install. Row cap in code is 1,048,575 data rows (`Excel row limit exceeded...`). Cell text cap 32,767. |
| **Export participant SQL** | New `.sqlite` with that participant only |
| **Export database** | SQLite backup snapshot of the whole collection |
| **Export original files** | ZIP of one finalized, inactive run, original streams and checksums |
| **Download review CSV** | Task summary for the selected session, from the browser |
| **Download export** | Browser download of the prepared file. Another copy stays in `<data root>/exports/` |

Download route: `GET /api/download/<id>?token=`. Content types are zip, xlsx, or `application/vnd.sqlite3`. A missing id or file returns 404 `Export no longer available. Create a new export.` The server will not read an arbitrary path. Exports describe the last indexed copy. Finish the test or press **Refresh** before exporting it. Export does not pause a running test.

**Remove participant data** / **Remove selected run** delete SQL rows and remember the directory in `removed_recordings` so Refresh will not import it. Original files and older export copies remain. An open recording cannot be removed (`collection.Remove` refuses the active directory).

### Disk and sync folders

Keep the clone and `collection data` outside OneDrive, Dropbox, or similar. The setup guide says Unity's `Library/` and generated files are local, and Git is the sync path for source. The writer refuses reparse points under the data root. Two ELTS writers on one shared folder are not supported (`SessionLogWriter` says deployment storage must not be shared with arbitrary writers).

## Python analysis

Reference: [synthetic analysis ingest](../modules/analysis.md), [verify a run](../../tools/logging/README.md), [replay](../modules/replay-reader.md).

Analysis reads a **closed** run. A checkpoint file is recovery evidence. It is not a substitute for `session-summary.json`. Dashboard metrics (`elts.task-metrics.v1`) are a separate descriptive path and are not this score.

From the repository root:

```powershell
python -m tools.logging.verify_run "collection data\<run-folder>"
python -m tools.logging.verify_run "collection data\<run-folder>" --report test-results\verify-report.json
python analysis\run_ingest.py "collection data\<run-folder>" --calibration analysis\fixtures\synthetic-calibration.json --output analysis-output.json
```

`analysis/run_ingest.py` inserts `analysis/src` on `sys.path`. Direct module use needs `PYTHONPATH=analysis/src`. The package is not installed. Tests:

```powershell
python -m unittest discover -s analysis/tests -v
```

Ingest prints the report JSON and exits 0, or prints `INGEST FAILED: ...` to stderr and exits **2**.

### What the tools reject

`verify_run` and ingest both require the four products, UTF-8, no blank lines, strictly increasing `sequence`, nondecreasing `monotonicTicks`, checksums equal to `session-summary.json` `checksumsSha256`, and counts equal to `counts.writtenSamples` / `writtenEvents` / `writtenTargets`. A missing file is `Missing required product` or `missing required product`. A bad checksum is `SHA-256 does not match session summary` or `<file> checksum does not match summary`. An unfinished summary is `session-summary.json does not represent a complete successful run` or `session summary is not complete`. Duplicate JSON keys, non-finite numbers, and a mixed v1/v2 target stream are rejected. Valid tracking must be `Connected` with a pose; any other validity must have `"pose": null`.

The output path must be a new file. Ingest refuses to overwrite, and it refuses to place the report inside the run directory or on top of the calibration file (`derived output must not replace a raw product or calibration`, `derived output already exists; refusing overwrite`).

### Calibration argument

`--calibration` is required. The committed file `analysis/fixtures/synthetic-calibration.json` has `calibrationId` `synthetic-identity-v1`, a zero muzzle offset, bore `(0,0,1)`, and an identity quaternion. Those are configuration values. They are not a measured zero. The fixture written beside a recording is a different file; the ingest command does not pick it up unless you pass that path, and the run verifier does not check it automatically. A quaternion that is not unit length within `1e-8` fails. Numeric strings and booleans are rejected.

The rig's own synthetic calibration id is `synthetic-identity` in `synthetic-rig.json`, which is a different string from the analysis fixture's `synthetic-identity-v1`.

### Scores that come back unavailable

Standard scoring needs `BlockStarted` and `BlockEnded` exactly **300 seconds** apart (3,000,000,000 ticks of 100 ns) and no `BlockAborted` on that block. Dashboard duration edits, pause/resume, and early stop are accepted only when `provenance.synthetic` is true, and those blocks are reported as `scoreStatus: unavailable` with reason `flexible synthetic test; excluded from the standard uninterrupted 300-second score`. A run with no complete marker pair reports `no complete BlockStarted/BlockEnded interval in event stream`. `Flexible development timing requires synthetic provenance` means those events appeared on a non-synthetic summary.

`TargetDestroyed` is the only primary score event. `Despawned` removes an aim candidate and does not score. Samples with either tracker disconnected, invalid, or missing a pose increment `invalidSamples` and are left out of aim error.

Replay in the player (`RecordedReplay`) is a viewer. It also requires the published summary and will not load the pending file. Its line cap is 65,536 characters. A hash or schema mismatch rejects the whole load ([replay](../modules/replay-reader.md)).

## Simulated Jetson controller

References: [link module](../modules/elts-link.md), [session adapter](../modules/elts-session.md), [Jetson directory](../../jetson/README.md), schema [`schemas/protocol/elts-development-synthetic-v1.schema.json`](../../schemas/protocol/elts-development-synthetic-v1.schema.json).

### What you can exercise today

The protocol version string is `development-synthetic-v1`. Commands are `HELLO`, `ARM`, `DISARM`, `START`, `STOP`, `PING`, `STATUS`, with replies `ACK`, `NACK`, and `STATE`. The state machine is `Idle → Armed → Active`. Only `START` sets the simulated `LedPermission` true. `STOP`, a heartbeat timeout, a duration-plus-guard timeout, and `SimulateEStop` clear it. An E-stop latch answers later `START` with NACK `estop_latched` until `ReleaseSimulatedEStopForFixture`. Reconnect (`SimulateReconnect`) returns to `Idle`, requires `HELLO` again, and does not resume `Active`.

Other NACK codes from `SimulatedEltsController`: `unsupported_version`, `invalid_state`, `sequence_conflict`, `replay_state_changed`, `sequence_expired`, `unknown_command`. `NullEltsLink` NACKs with `null_link`. Lines longer than **4096** characters are rejected. Unknown JSON fields are rejected, so the codec cannot carry a head pose, a target, a turret angle, or a per-LED command. That prohibition is also the safety rule in [safety](../safety/README.md).

Fixture timing defaults, not approved safety values: heartbeat **3 s**, extra maximum-on time **30 s**, replay cache **128** (`EltsLinkRuntimeOptions`). The session adapter retries a delivery exception at most **2** times by default (`DefaultMaximumCommandAttempts`, allowed 1–8) and reuses one sequence. A NACK is not retried. Logged event types are `EltsCommand`, `EltsAck`, `EltsNack`, `EltsState`, `EltsOffset`, and `EltsLinkLoss`.

For a WE block the adapter requires `HELLO`, `ARM`, and `START` to be acknowledged before the block runs. `Tick` advances the mock and fails the session if permission drops or the mock hits its time limit. The default NE mode is `Idle`: the adapter sends `DISARM` before an NE block. `Armed` is the other dropdown value on the Unity workbench (`neLinkMode`), default `Idle`. Neither mode is a study decision. The workbench link line ends with `D-10 pending.` The browser station hides that workbench; the browser page does not show that sentence.

`EltsClockOffsetEstimate` stores Unity sent ticks, Unity ack ticks, and Jetson ticks and computes `(sent + ack) / 2 - jetson`. The session event `EltsOffset` stores raw endpoint-minus-Unity ticks. Neither value is an approved synchronization result.

### Checks that stay in process

```powershell
dotnet run --project tools/runtime-tests/EltsLinkChecks.csproj -p:NewtonsoftPath="C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\Managed\Newtonsoft.Json.dll"
```

After the project has been imported so `unity/Library/PackageCache` contains exactly one `com.unity.nuget.newtonsoft-json` **3.2.2**:

```powershell
$jsonPackages = @(Get-ChildItem unity/Library/PackageCache -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    Where-Object { (Get-Content (Join-Path $_.FullName 'package.json') -Raw | ConvertFrom-Json).version -eq '3.2.2' })
$jsonAssembly = Join-Path $jsonPackages[0].FullName 'Runtime/Newtonsoft.Json.dll'
dotnet run --project tools/runtime-tests/EltsSessionChecks.csproj "-p:NewtonsoftPath=$jsonAssembly"
```

These projects need the .NET SDK in `global.json`. They do not open a socket. `config/toolchain.json` records `jetsonBaseline` as `pending device inspection`.

### Symptom: the apparatus lights never change

**Likely cause.** No code path in the player drives a lamp. `LedPermission` is a Boolean on the simulated controller. The development package doc says the player never connects to the physical apparatus.

**Check.** In the recording, search `events.ndjson` for `EltsState` and `EltsNack`. A simulated failure names one of the NACK codes above. There is no serial port, UDP port, or host configured for a Jetson. `config/local.json` `eltsEndpoint` cannot be set to an IP; staging and `DevelopmentConfiguration` reject it.

**Fix.** For a software rehearsal, read the NACK and the session failure text. For a hardware light, stop here and use [GAP-HW-JETSON-TRANSPORT](#gap-register) and [GAP-HW-ELTS-LIGHTS](#gap-register). Do not point the endpoint at a device until D-03 and a controller contract exist; the current loader will refuse the file, and the station would still construct the mock if the string were one of the two allowed values.

## Hardware

Nothing in this section has been run against the equipment that arrived on 21 October 2026. The software described above will not detect that equipment. [Hardware follow-up](hardware-follow-up.md) is the list of missing physical evidence and the roles named for it. Generic vendor steps are labeled. They are not ELTS procedures.

### Trigger

**What the repo establishes.** The scored trigger on the browser station is a mouse left-button release inside the participant image during an armed or running block, matched to the acquisition pair (`TryCaptureForTrigger`). The synthetic source emits one event on release after a hold, with `triggerLockoutSeconds` 0.02, and clears edge state on a dropout so a reconnect cannot invent a shot. `SessionReadinessReport` does not mention a trigger. Tracking checks "do not verify ... trigger timing" ([tracking](../modules/tracking.md)). Future hardware is specified only as "an `ITrackingSource` and a device trigger adapter" at the desktop boundary.

**Symptom.** A physical switch, pedal, or controller trigger does nothing, or a mouse click fires when the hardware trigger does not.

**Cause in this build.** No HID, serial, or OpenVR button path is wired. Vendored OpenVR defines `k_EButton_SteamVR_Trigger`, and the player never calls OpenVR init (`dependency.json`: `runtimeInitialization` is `Not performed; synthetic mode never initializes OpenVR`).

**Check.** Confirm the participant window title is `Shoot to begin` or the block is running, the pointer is inside the image, and the dashboard is not holding a canceled click (click again after focusing the game window). Confirm `events.ndjson` gains a `ShotFired` from that mouse release. A silent physical trigger will not appear there.

**Fix.** Use the mouse path for software rehearsal. A hardware trigger needs the adapter described in the desktop-input doc, plus the evidence in the follow-up register.

> **GAP-HW-TRIGGER.** The repo does not name the trigger's model, connector, debounce, or timestamp source. No owner is named for the trigger specifically. The nearest named roles are the tracking/display engineer (exercise physical tracking) and the research lead (study acceptance) in [hardware follow-up](hardware-follow-up.md).

### Vive trackers

**What the repo establishes.**

- Two logical trackers, roles `head` and `weapon`, serial strings in `trackerSerials`. The synthetic template uses `SYNTHETIC-HEAD` and `SYNTHETIC-WEAPON`. They must be distinct non-empty strings. The desktop source stamps those strings onto mouse poses. It does not enumerate USB devices.
- Readiness text, if the flags were actually false, would be `Both bound trackers must be valid for at least 2 seconds.` The player sets the flag true after two seconds of valid *software* poses (`ValidTrackingSeconds` is 2).
- Sample fields: `connection` `Disconnected` | `Connected` | `Reconnecting`; `validity` `Unavailable` | `Valid` | `OutOfRange` | `DriverFault` | `RejectedNativePose`. `Valid` requires `Connected` and a pose. Every other validity requires a null pose (`schemas/logs/samples.v1.schema.json`).
- `TrackingCoordinateConverter.Convert` is the only OpenVR-to-Unity conversion: reflect native Z, meters, +X right, +Y up, +Z forward. No player calls it, because no player calls OpenVR.
- Vendored SDK: Valve OpenVR **2.12.14**, commit `91825305130f446f82054c1ec3d416321ace0072`, Windows x64 DLL under `unity/Assets/ThirdParty/OpenVR/`. Preload is off. `python tools/dependencies/verify_openvr.py` checks bytes and does not load the DLL ([OpenVR dependency](../modules/openvr-dependency.md)).
- The illustration in [elts-simulation](../../elts-simulation/README.md) says "a forehead tracker and a mock-weapon tracker" from the briefing. It also says room dimensions and placement are demonstration values.

**Symptom.** SteamVR shows trackers, and ELTS still shows synthetic motion or the serials `SYNTHETIC-HEAD` / `SYNTHETIC-WEAPON` in `samples.ndjson`.

**Cause in this build.** There is no tracker poll. `samples.ndjson` `trackerId` will be the configured serials, which are the synthetic literals unless you change and restage the rig file. Changing the strings only changes the label. It does not bind a device.

**Check.**

```powershell
python tools/dependencies/verify_openvr.py
```

Open one `samples.ndjson` line and read `head.trackerId`, `weapon.trackerId`, `connection`, and `validity`. On the browser station those poses follow the mouse, and validity stays `Valid` while the publisher is fresh. Dashboard **Readiness & diagnostics** has a row **Devices and displays** with status `Emulated` and the detail `Display and SteamVR readiness are emulated`. The hidden Unity workbench uses the same word (`OperatorDashboardLists`).

**Fix.** Keep using the synthetic serials for rehearsal. Binding real serials is the follow-up item "Bind actual trackers; measure positions...". After a real adapter exists, invalid rows must stay invalid; do not hand-edit NDJSON to force `Valid`, or checksums and ingest will fail.

> **GAP-HW-VIVE-MODEL.** The repo does not name a Vive generation, tracker count beyond the two roles, mount, or the SteamVR serial format to copy into `trackerSerials`. Who can answer: the rig and calibration operator ([hardware follow-up](hardware-follow-up.md)).

> **GAP-HW-VIVE-PAIR.** Pairing, dongle or USB layout, and the SteamVR role assignment are not described. Who can answer: the tracking/display engineer. The OpenVR module doc says enumeration, serial binding, and polling are separate tasks and that the testing equipment was unavailable when that doc was written.

> **General guidance, not an ELTS procedure.** Valve's upstream README, vendored at `unity/Assets/ThirdParty/OpenVR/UPSTREAM-README.md`, says the OpenVR runtime is SteamVR under Tools in Steam and links to `https://steamvr.com`. ELTS does not launch Steam, install SteamVR, or pair a tracker.

### SteamVR base station

**What the repo establishes.** `SessionReadinessReport` adds `SteamVR is not running.` when `SteamVrRunning` is false. The desktop player passes `true` after the synthetic pose check and labels the substitution emulated. `config/toolchain.json` `validation.studyMachineBaseline` is `pending physical commissioning`. The rendering doc says "No base station is attached" and lists "real tracking and base station diagnostics" as deferred. The simulation places one base station above the display and says the direction is symbolic, not a coverage volume. The fixed banner text `Base station not connected` is not a sensor reading.

**Symptom.** Base stations are powered and ELTS never reports them. Or the banner says they are not connected even though SteamVR is happy.

**Cause in this build.** ELTS does not query SteamVR or a base station. The banner is static. A doctor pass does not look for a SteamVR process.

**Check.** There is no ELTS log line, port, or config key for a base station. The only related config is the unused display indices and the synthetic rig.

**Fix.** For rehearsal, ignore the hardware and read tracker validity in the NDJSON as software validity. Physical coverage, occlusion, and dropout recovery are the tracking/display row in the follow-up register.

> **GAP-HW-STEAMVR.** The repo does not name a SteamVR version, process name, log path, or a command ELTS should run. Who can answer: the tracking/display engineer. The toolchain note says the study Windows, GPU, driver, and SteamVR baseline is pending commissioning ([toolchain](toolchain.md)).

> **GAP-HW-BASESTATION.** Model, count, power, channel, and mount are not in the repo. The simulation's single overhead station is an illustration. Who can answer: the tracking/display engineer.

### Wearable eye tracker

**What the repo establishes.** The words "eye-tracker" appear as planned work, not as a driver:

- README planned work: coordinate eye-tracker data with participant, session, and scenario ids. Status: not implemented.
- [Tutorial outline](../participant-tutorial-script-outline.md) (19 October 2026, not a script and not software): a future setup cue mentions an eye-tracker. No step list.
- The simulation README: "Auxiliary physiological and eye-tracking instruments are omitted because their placement and models are unspecified." Eye markers in that page are positions, not gaze.

The calibration "eye" is a different thing: `Eye` input is Left or Right plus a three-component tracker-to-eye offset in meters ([calibration](../modules/calibration.md)). `headEyeOffsetM` in the synthetic rig is `[0,0,0]`. The rendering eye marker is derived from the head pose. None of these read a wearable.

**Symptom.** The eye tracker is worn and no ELTS file contains gaze.

**Cause in this build.** There is no gaze stream, SDK, COM port, or clock mapping.

**Check.** Recording products are only the four logging files plus checkpoints, `participant.json`, and the optional calibration sidecar. None has a gaze schema under `schemas/logs/`.

**Fix.** Record the wearable with its own vendor tool if you need a file today, and keep it out of the ELTS raw folder so you do not break checksums. Joining it to a session id is the unimplemented README item.

> **GAP-HW-EYE.** Model, placement, SDK, sample rate, and file format are absent. No role in [hardware follow-up](hardware-follow-up.md) is assigned to a wearable eye tracker. The nearest text is the README integration checklist and the tutorial outline.

> **General guidance, not an ELTS procedure.** Vendor software for a wearable may show its own battery, fit, and recording status. That status is not read by ELTS.

### Heart-rate monitor (Polar)

**What the repo establishes.** "Heart-rate monitor" appears in the same two places as the eye tracker: the README integration checklist and the tutorial outline's setup cue. NASA-TLX is named there as a questionnaire to associate with a segment, also unimplemented. A search of the repo finds no use of the name Polar, no BLE or ANT handle, and no heart-rate schema.

**Symptom.** A Polar strap or watch is on the participant and ELTS has no heart-rate column.

**Cause in this build.** Nothing opens a heart-rate device or writes beats into `events.ndjson`.

**Check.** The Data view metric list (hits, shots, aim error, and the rest in [data collection](../modules/data-collection.md)) has no heart-rate field. `task_results` will not grow one after Refresh.

**Fix.** Same as the eye tracker: a vendor recording is outside this repo's raw products. Do not drop it into the run folder before `session-summary.json` is written; extra files are not part of the checksum set, but do not edit the NDJSON to insert beats.

> **GAP-HW-POLAR.** The repo never names Polar, a model, a radio, a pairing step, or an output file. Calling the device Polar in this guide follows the request to cover that instrument; it is not a repository fact. Who can answer: no hardware-follow-up row exists. The nearest text is the README "Study data integration" item.

> **GAP-SW-NASATLX.** No form, response file, or id join exists. The tutorial outline lists the post-study form as unfinished. Who can answer: the research lead, for the protocol; the repo does not name a software owner for the form.

> **General guidance, not an ELTS procedure.** A Polar device is typically paired in its own app or a BLE tool. ELTS does not perform that pairing.

### ELTS apparatus and Jetson

**What the repo establishes.**

- The apparatus is described as built. Testing equipment was unavailable when the safety and follow-up docs were written. This guide adds that arrival on 21 October 2026 is still not a validation record.
- Unity may send lifecycle commands only. It must not send poses, world targets, turret angles, or per-LED commands. A normally closed physical E-stop and device-owned safeguards stay independent of Unity ([safety](../safety/README.md)).
- The in-repo controller is the simulated one in [the previous section](#simulated-jetson-controller). `jetson/README.md` states the directory has no listener, wrapper, GPIO, network, serial, LED, or E-stop code, and no copy of the existing controller. `jetson/config/`, `jetson/scripts/`, `jetson/src/elts_link/`, `jetson/systemd/`, and `jetson/tests/` are empty placeholders.
- The fixture schema is explicitly not compatible with the built controller. Field names and tick units are development conventions.
- Before a real wrapper: the ELTS code owner must supply controller source or a reviewed wire contract, the LED-permission feedback point, and the selected D-03 transport. D-10 must choose NE behavior. D-13 must decide exposure and maximum-on time. Link timing, shutdown, E-stop, and light behavior need bench evidence (`jetson/README.md`).
- The simulation, from the briefing, draws three turrets (two outer LED units and a central Brio) and says that drawing does not implement the Brio/Jetson vision pipeline. Light lines are symbolic, not an exposure calculation.
- `toolchain.json` `validation.jetsonBaseline` is `pending device inspection`.

**Symptom.** The Jetson is powered, or the LEDs are dark, and the dashboard still runs a session.

**Cause in this build.** The session talks to `SimulatedEltsController` in the same process. Network and serial settings do not exist. A WE block can show a simulated permission true in the hidden workbench (`simulated permission true`) with no lamp attached.

**Check.**

1. `config/local.json` `eltsEndpoint` is `mock://elts` or `null://elts`.
2. `events.ndjson` contains `EltsCommand` / `EltsAck` / `EltsState` produced locally. `EltsOffset` ticks are the in-process clocks.
3. `jetson/` has no service file to start. There is no port to scan in this repo.
4. `python tools/config/stage.py --check` succeeds only for the synthetic bundle.

**Fix.** Use the simulated link for software rehearsal and read NACK codes if a WE block fails immediately. Hardware control waits on the code owner and on D-03, D-10, and D-13. The shipped package "does not install device software, open a controller endpoint, energize hardware, or authorize physical work" ([hardware follow-up](hardware-follow-up.md)).

> **GAP-HW-JETSON-TRANSPORT (D-03).** Transport is undecided. The repo lists no serial device, baud, UDP port, host, MQTT topic, or other wire. `elts-link.md` says the checks have no serial, UDP, MQTT, network, device, process, GPIO, servo, camera, light, or physical E-stop access. Who can answer: the ELTS code owner (source or reviewed wire contract and the LED-permission feedback point) and the controller engineer with the research lead ([hardware follow-up](hardware-follow-up.md) controller row).

> **GAP-HW-JETSON-OS.** No OS image, login, hostname, or systemd unit is recorded. `jetsonBaseline` is `pending device inspection`. Who can answer: the controller engineer; the repo does not name a separate Jetson-image owner.

> **GAP-HW-ELTS-LIGHTS.** Which LEDs exist, how `LedPermission` maps onto them, and how the Brio is attached are not specified outside the simulation's briefing sketch. Who can answer: the ELTS code owner for the permission feedback point; the controller engineer for behavior on the rig.

> **GAP-HW-ESTOP.** The safety doc requires a normally closed E-stop independent of Unity. Connector, pin, and the bench procedure are not in the repo. `SimulateEStop` is a software latch and "models neither wiring nor a real E-stop reset procedure." Who can answer: the safety lead and the controller engineer.

> **GAP-HW-SYNC (D-12).** No approved way to align the Unity clock with a Jetson clock, an eye tracker, or a heart-rate monitor. Offset metadata in the mock is not that method. Who can answer: the timing engineer and the research lead.

> **GAP-HW-VALIDATION.** No file records a bench result for the equipment that arrived 21 October 2026. Who records acceptance: the research lead, the safety lead, and the maintainer ([hardware follow-up](hardware-follow-up.md) study-readiness row).

## Open investigator decisions

These change what a "wrong" session looks like. They are open in the source. They are not hidden settings you can flip to match the hardware.

| Id | What the repo says | What you will see until it is decided |
| --- | --- | --- |
| **D-03** | Unity-to-Jetson transport. A real adapter "requires D-03 and controller-owner review" (`EltsLinkContracts`). | No socket or serial port. See [GAP-HW-JETSON-TRANSPORT](#gap-register). |
| **D-04** | Study motion for moving targets. `ScenarioMovementFactory` says MT is a seeded provisional random walk until D-04 defines study motion. `config/development/scenario.json` sets `mtMotion` to `sine` and `provisionalDevelopmentValues` true. `DevelopmentScenarioAdapter` keeps that sine string as provenance and does not silently replace it with `RandomWalk`. `RequiresD04MotionResolution` is true when the configured name is not `RandomWalk`. Nothing in the player reads that flag. | The **visible** desktop session does not run sine or the generic random walk. `DevelopmentSessionScenario` passes movement name `LibraryRouteSequence` for `*_MT` and `FixedLibrary` for fixed blocks, then draws `LibraryTargetPopulation` (`library-routes-v3`, `library-fixed-poses-v2`). Dimensions are "not approved D-04 tuning." Generic `RandomWalk` runs only in `tools/runtime-tests/ScenarioChecks.cs`. If targets follow library routes, that is the current player, not a resolved D-04. |
| **D-05** | The string `D-05` does not appear. | [GAP-DEC-D05](#gap-register). |
| **D-06** | "D-06 remains undecided." `ShowHitVisual`, `PlayShotSound`, and `ShowScore` default false. | No code reads those flags, so there is no hit flash, shot sound, or score overlay from them. Ammo and hit counts on the HUD are separate labels. Silence is the current build, not a failed audio device. |
| **D-07** | The string `D-07` does not appear. | [GAP-DEC-D07](#gap-register). |
| **D-08** | The string `D-08` does not appear. | [GAP-DEC-D08](#gap-register). |

Related decisions the code does name, because they change link and timing symptoms:

| Id | Effect on troubleshooting |
| --- | --- |
| **D-10** | NE behavior is not chosen. Default fixture mode `Idle` disarms the mock before NE. `Armed` is a workbench option only. A dark lamp during NE, even on a future controller, cannot be judged until D-10. The workbench shows `D-10 pending.` |
| **D-12** | Cross-device sync is not chosen. See [GAP-HW-SYNC](#gap-register). |
| **D-13** | Exposure and maximum-on time are not chosen. The 30 s extra guard and the 3 s heartbeat are fixture defaults. A timeout in the mock is not a measured exposure limit. |
| **D-17** | Calibration approval limits are not the development thresholds. See [GAP-DEC-D17](#gap-register). |

> **GAP-DEC-D05.** Investigator decision D-05 is not named in code, schemas, or docs, so this guide cannot say what it decides or which symptom it would explain. Who can answer: the investigators who hold the decision list. The repo does not point to that list.

> **GAP-DEC-D07.** D-07 is not named. Same limitation and same source.

> **GAP-DEC-D08.** D-08 is not named. Same limitation and same source.

## Gap register

| Id | What is missing | Who can answer it |
| --- | --- | --- |
| GAP-HW-DISPLAY | Study display technology, resolution, Windows mapping, and measured screen corners. Config display indices are validated and unused at runtime. Rig geometry is `synthetic_unmeasured`. | Rig and calibration operator; tracking/display engineer |
| GAP-DEC-D17 | Approved calibration limits and the physical capture procedure behind them. D-17 is only named as not being the development thresholds. | Calibration operator and research lead |
| GAP-HW-TRIGGER | Physical trigger model, connector, and timing path. | No trigger-specific role. Nearest: tracking/display engineer; research lead for acceptance |
| GAP-HW-VIVE-MODEL | Vive generation, mounts, and the serial strings to put in `trackerSerials`. | Rig and calibration operator |
| GAP-HW-VIVE-PAIR | Pairing, dongle or USB, and SteamVR role assignment. OpenVR is vendored and never initialized. | Tracking/display engineer |
| GAP-HW-STEAMVR | SteamVR version, process, log, and whether ELTS should start it. The player forces `SteamVrRunning=true`. | Tracking/display engineer. Baseline pending in `config/toolchain.json` |
| GAP-HW-BASESTATION | Base-station model, count, power, channel, and mount. The on-screen "not connected" line is static. The simulation's overhead station is illustrative. | Tracking/display engineer |
| GAP-HW-EYE | Wearable eye-tracker model, placement, SDK, and file format. Calibration "eye" is a tracker-to-eye offset. | No follow-up row. Nearest text: README integration checklist and the tutorial outline |
| GAP-HW-POLAR | Polar is not named. Heart-rate model, radio, pairing, and file format are absent. | No follow-up row. Nearest text: README "Study data integration" |
| GAP-SW-NASATLX | Questionnaire content, storage, and join to session ids. | Research lead for the protocol; no software owner named |
| GAP-HW-JETSON-TRANSPORT | D-03 transport: no serial, UDP, host, port, or MQTT. Endpoint whitelist is `mock://elts` and `null://elts`. | ELTS code owner; controller engineer and research lead |
| GAP-HW-JETSON-OS | Jetson OS, host, login, and service. `jetsonBaseline` is `pending device inspection`. | Controller engineer; no separate image owner named |
| GAP-HW-ELTS-LIGHTS | LED map, permission feedback point, and Brio connection. The three-turret picture is a briefing illustration. | ELTS code owner; controller engineer |
| GAP-HW-ESTOP | Physical E-stop connector, pin, and reset procedure. The software latch is not that circuit. | Safety lead and controller engineer |
| GAP-HW-SYNC | D-12 method for aligning Unity, Jetson, eye, and heart-rate clocks. | Timing engineer and research lead |
| GAP-HW-VALIDATION | A recorded bench result for hardware present on 21 October 2026. The repo still says testing equipment is unavailable. | Research lead, safety lead, and maintainer |
| GAP-DEC-D05 | Decision D-05 is not in the repo. | Investigators who hold the decision list; the list is not in the repo |
| GAP-DEC-D07 | Decision D-07 is not in the repo. | Same |
| GAP-DEC-D08 | Decision D-08 is not in the repo. | Same |

## Related docs

- [User guide](user-guide.md) and the README install section for the normal click path
- [Setup troubleshooting](multi-machine-setup.md#if-something-goes-wrong) for licenses, Git, and first-run install
- [Developer commands](developer-quick-start.md)
- [Data collection](../modules/data-collection.md), [logging](../modules/logging.md), [analysis](../modules/analysis.md)
- [Dashboard](../modules/operator-dashboard.md), [desktop input](../modules/desktop-input.md), [sessions](../modules/session.md), [scenarios](../modules/scenario.md)
- [Link](../modules/elts-link.md), [session link](../modules/elts-session.md), [Jetson boundary](../../jetson/README.md)
- [Hardware follow-up](hardware-follow-up.md), [safety](../safety/README.md)
