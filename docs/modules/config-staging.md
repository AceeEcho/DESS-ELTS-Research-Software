# Synthetic configuration staging

`tools/config/stage.py` is the BOOT.S017 configuration boundary. It reads only
the explicit paths in `config/staging.json`, validates each object against its
strict schema, applies cross-field geometry and machine-path checks, and emits
`unity/Assets/StreamingAssets/config-generated/effective-config.json` and
`manifest.json`. The effective document has the stable root shape
`schemaVersion`, `mode`, `studyReady`, `runtime`, `rig`, `scenario`, `session`,
and `machine`.

The effective JSON is canonical UTF-8 with sorted keys, compact separators, and
one LF newline. Its SHA-256 is recorded in the manifest along with raw source
hashes, including `sourceRawSha256.staging`. The manifest names
`schemas/effective.schema.json` and records `schemaFilesSha256` for that strict
schema and each whitelisted component schema. A C# loader must hash the exact staged effective bytes before parsing;
it must not reserialize to reproduce the hash. Writes use temporary files and
`os.replace`; identical bytes are left untouched so repeated staging preserves
mtime. `--check` rejects missing, stale, or extra generated files.

This lane is synthetic-only: study acceptance remains false, calibration is
explicitly `synthetic_only`, and all geometry is marked
`synthetic_unmeasured`. A machine-specific `config/local.json` is optional and
is limited to the local schema; it cannot override study, rig, safety, or
calibration fields.

```text
python tools/config/stage.py --write
python tools/config/stage.py --check
python tools/config/stage.py --write --output unity/Assets/StreamingAssets/config-generated
```
