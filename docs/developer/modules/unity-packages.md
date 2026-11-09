# Unity package pins

`config/unity-packages.json` is the editable required-package manifest, checked
by `schemas/build/unity-packages.schema.json`. Unity's own package manifests and
lock are generated through its Package Manager API. Do not hand-edit them.

The selected Unity 6000.3.23f1 Editor bundles URP 17.3.0 and Test Framework 1.6.0.
Newtonsoft 3.2.2 is a registry package verified against the official Unity
registry. All three are direct exact pins. The offline audit rejects manifest,
lock, source-kind or Editor-version drift and rejects external and built-in XR.

```text
python tools/dependencies/verify_unity_packages.py
python -m unittest tools.dependencies.test_unity_packages
```

To reconcile packages after an intentional reviewed configuration change, first
validate the schema, then invoke the exact Editor with `-batchmode`, the project
path, and `-executeMethod Elts.Editor.PackageSetup.InstallRequired`. Do not pass
`-quit`: UPM is asynchronous and the completion callback exits the process.
Run the offline audit and a fresh Editor import afterward. Tests and builds are
separate acceptance steps.

The Config assembly's package version define keeps it excluded during initial
bootstrap before Newtonsoft is installed; it must be present in the subsequent
Unity import report. Missing packages must never become a study fallback. The
synthetic runtime remains incapable of study readiness after successful restore.
