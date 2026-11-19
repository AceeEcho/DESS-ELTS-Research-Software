# Development ELTS session adapter

`SessionEltsLinkAdapter` is a pure, synthetic-only bridge from the session state
machine to the existing `IEltsLink` fixture protocol. It is not a Unity-to-Jetson
integration and cannot energize hardware. A real controller integration remains
subject to the controller contract, D-03 review, physical fail-safe validation,
and the approved WE/NE study behavior.

The session engine calls `PrepareBlock` for every condition. For WE it requires
successful synthetic `HELLO`, `ARM`, and then `START` acknowledgement before the
block starts. `Tick` advances the mock controller, records heartbeat state, and
fails the session if permission is lost or the controller reaches its time limit.
Failure retains the engine's existing best-effort stop behavior.

The default `DevelopmentNeLinkMode.Idle` disarms the mock link before an NE block.
`Armed` is available only as an explicit fixture option. Neither mode determines
D-10 or authorizes a study behavior.

Each sent command and reply is a typed `SessionEvent`: `EltsCommand`, `EltsAck`,
`EltsNack`, `EltsState`, `EltsOffset`, or `EltsLinkLoss`. These use the injected
shared monotonic clock and an injected sequence. `EltsOffset` retains raw observed
endpoint-minus-Unity ticks; it is not a synchronization estimate or study timing
claim. Delivery-exception retries are bounded and reuse one protocol sequence,
allowing the mock controller's idempotence rules to apply. NACKs are semantic
protocol failures and are never retried. A reconnect latches a loss; only a new
explicit block-attempt context can clear it, and the adapter never resumes the
previous block.

Run the synthetic check from the repository root with:

After importing the project with its pinned packages, use PowerShell:

```powershell
$jsonPackages = @(Get-ChildItem unity/Library/PackageCache -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    Where-Object { (Get-Content (Join-Path $_.FullName 'package.json') -Raw | ConvertFrom-Json).version -eq '3.2.2' })
if ($jsonPackages.Count -ne 1) { throw 'Import the pinned Newtonsoft 3.2.2 package first.' }
$jsonAssembly = Join-Path $jsonPackages[0].FullName 'Runtime/Newtonsoft.Json.dll'
dotnet run --project tools/runtime-tests/EltsSessionChecks.csproj "-p:NewtonsoftPath=$jsonAssembly"
```

The check verifies only the synthetic adapter and mock controller. It does not
verify Unity-Jetson communication, physical output, E-stop behavior, timing, or
the final WE/NE study decision.

## Windows development player with the isolated Jetson simulator

The USB sender is off by default. Keep the tracker movement outputs isolated
while the player talks to the simulated receiver.

On cadet, start the mock receiver and wait for
`Ready on /dev/ttyGS0 with simulated outputs`.

```sh
cd /home/cade/ELTS_Code && python3 -B -m usb_lifecycle_mock.pty_mock --usb
```

On Windows, confirm the present CDC-ACM port belongs to USB VID `0955` and
PID `7020`. Use `COM4` only if that is the current assignment. Start ELTS from
the repository root with:

```powershell
.\START-ELTS.cmd -TrackerSimulatorCom COM4
```

For a Windows development build with this feature, pass `-eltsTrackerSimulatorCom COM4`
to `ELTS-Synthetic.exe`. The administrator run panel shows the selected port and
sender status. The port can be open while the tracker is idle. The sender
negotiates a fresh run only on a live WE entry. A lost connection cannot resume
that segment. Omit the launch option on the next startup to leave USB disabled.

### Automated bench test

The separate Unity PlayMode bench follows the same scenario lifecycle with
synthetic inputs and a temporary test recording. Use an idle copy of the
project staged for synthetic tests.

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe'
$results = New-Item -ItemType Directory -Force 'test-results/tracker-usb-bench'
& $unity -batchmode -noaudio -projectPath (Join-Path $PWD 'unity') -runTests `
  -testPlatform PlayMode -testFilter 'WindowsTrackerSimulatorBench' `
  -eltsTrackerSimulatorCom COM4 `
  -testResults (Join-Path $results.FullName 'bench.xml') `
  -logFile (Join-Path $results.FullName 'bench.log')
```

A passing test observes `HELLO → IDLE`, `START → ACTIVE`,
`HEARTBEAT → ACTIVE`, and `STOP → IDLE` from the simulator. The test reports the
selected port missing if Windows cannot match the live device. With the setting
omitted, the bench is skipped. Stop the Jetson simulator with Ctrl+C after the
test. These results cover the simulated gate and USB messages.
