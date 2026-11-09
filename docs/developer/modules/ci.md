# Software CI

The repository workflow runs source hygiene, local documentation links, toolchain
and dependency checks, and Python tests on Windows and Ubuntu. Run it locally:

```powershell
python tools/ci/check.py --baseline
```

The suites cover shared JSON validation, configuration staging, setup helpers,
dependencies, build/package integrity and log validation. No planning catalog,
task events or milestone records are required by these checks.

CI runs on pull requests and pushes to main; superseded runs are canceled.
Feature branches can use a pull request or manual dispatch. Workflow files use
JSON syntax (a YAML subset), pinned actions and read-only repository permissions.

Unity verification is a separate manual workflow for a Windows x64 runner labeled
`elts-unity-6000.3.23f1`, with that exact licensed editor and Windows Mono support.
It runs setup, EditMode/PlayMode tests, a build and product verification.
Local checks do not prove that a remote runner or Unity runtime test has passed.

Documentation validation checks local Markdown links across `docs/` and the root
guides; it does not check external websites or illustrative code paths.
