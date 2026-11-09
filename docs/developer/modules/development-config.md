# Development configuration loader

`Elts.Config.DevelopmentConfiguration.LoadDirectory` reads one staged bundle on
startup and returns immutable typed settings. Other runtime modules receive
these settings through their constructors; they do not reread arbitrary JSON.
The loader checks raw SHA-256 hashes before parsing, enforces the shared strict
schemas, rejects duplicate fields and malformed values, and applies cross-field
checks for rig identity, display geometry, tracker bindings and output paths.

Edit placement in `config/rig/templates/synthetic-rig.json`, scenario choices in
`config/development/scenario.json`, session defaults in `config/defaults/session.json`,
and runtime settings in `config/defaults/runtime.json`. The whitelist in
`config/local.example.json` describes optional machine overrides. Stage again
after edits. Coordinates use meters and the Unity room frame (+X right, +Y up,
+Z forward). Geometry remains explicitly synthetic and unmeasured.

This implementation only accepts synthetic mode, synthetic calibration and
mock/null ELTS endpoints. `StudyReady` always returns false and
`RequireStudyReadiness()` throws. Missing measured calibration cannot fall back
to an example rig. The bundle hashes detect corruption; they do not grant
approval or authenticate the party who wrote a bundle.

For standalone verification, supply the Newtonsoft assembly from the exact
verified Unity Editor installation:

```text
dotnet run --project tools/runtime-tests/ConfigChecks.csproj -p:NewtonsoftPath="<Editor>/Data/Managed/Newtonsoft.Json.dll" -- unity/Assets/StreamingAssets/config-generated
```

The console projects have separate output and intermediate directories and pin
C# 9. Configuration checks cover unknown/missing fields, invalid geometry,
calibration identity, study rejection, endpoints, corruption and both sides of
the same `1e-10` basis tolerance used by staging. Geometry checks cover local to
world and inverse transforms, known angles, projection and invalid inputs.
OpenVR handedness conversion belongs to the future Tracking boundary only.

Physical follow-up remains required for measured dimensions, serial bindings,
mount rigidity, display registration and real calibration. These checks do not
accept a study rig or pass a baseline gate. Unity compilation and runtime tests
are tracked separately in bootstrap.
