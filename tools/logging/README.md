# Logging tool entry point

Verify a completed local v1 run without modifying it or copying raw data:

```text
py -3 -m tools.logging.verify_run <run-directory>
py -3 -m tools.logging.verify_run <run-directory> --report test-results/dev04-verify-report.json
```

The verifier streams NDJSON products, caches committed schemas once per run, rejects duplicate keys and non-finite JSON values, validates each product row and the closure record, checks sequence/timestamp ordering, invalid-pose safety, declared counts, and SHA-256 checksums. It accepts only a complete successful run.

The portable runtime check is `../runtime-tests/LoggingChecks.csproj`. See `../../docs/modules/logging.md` for the v1 contract and real 30-minute soak command.
