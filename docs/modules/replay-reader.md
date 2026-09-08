# Recorded replay reader

`Elts.Rendering.RecordedReplay` is the bounded development viewer reader for a
completed `elts.session-summary.v1` run. It reads the three v1 NDJSON products
(`samples.ndjson`, `events.ndjson`, and `targets.ndjson`) as UTF-8, verifies each
raw product SHA-256 against the summary, and retains the summary hash and source
provenance. A pending summary is never loadable because only the published
`session-summary.json` is accepted.

The reader is strict: unknown or missing fields, duplicate JSON properties,
unsupported schema values, malformed identifiers, non-finite numbers, count or
hash mismatches, non-monotonic sequence/timestamp data, and invalid pose
payloads reject the complete load. Tracking status is preserved on each frame;
invalid or disconnected poses remain null and are never interpolated. The
reader does not apply prediction, so the logged weapon pose remains raw (SCI-006
and SCI-008).

Loaded arrays are cloned and exposed through value results. Target queries use
per-target timestamp histories and binary search, so repeated scrubbing does
not rescan a million-row target stream for every frame. This is a bounded viewer
implementation; large studies should use a streaming analysis reader.

The focused harness is portable and does not hardcode a Newtonsoft.Json path:

```powershell
dotnet run --project tools/runtime-tests/ReplayChecks.csproj `
  -p:NewtonsoftPath="<verified Unity Editor>/Data/Managed/Newtonsoft.Json.dll" `
  -- <optional generated-log-directory>
```

The optional directory can be a short generated log such as
`main/test-results/dev04-publication` or `synthetic-soak`. The harness always
creates additional temporary fixtures covering valid loading, invalid poses,
unsupported values, duplicate fields, non-finite values, missing products,
tampered hashes, constructor validation, lifecycle seek, and invalid sampling.
