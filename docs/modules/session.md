# Session runtime

`SessionEngine` is a pure, operator-driven state machine. It exposes the current state, condition, block index, rerun index, remaining logical-clock seconds, and whether targets may exist. It emits every transition through `ISessionEventSink`; a sink or WE-link failure enters `Failed` and prevents continued use.

`SessionReadinessReport` checks the required two displays, SteamVR, two serial-bound valid trackers for two seconds, configuration hashes, 2 GB of free space, live logging, and the WE link when a WE condition is assigned. Synthetic success never sets `StudyReady`.

`ISessionRecordingLifecycle` is an asynchronous seam for an adapter that reserves and starts an existing `LoggingRunDirectory`/`SessionLogWriter` run outside the UI thread. Reruns preserve the prior raw folder and carry an incremented rerun index; the engine never deletes or writes raw files.
