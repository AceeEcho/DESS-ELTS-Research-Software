# OpenVR dependency

The unmodified binding and Windows x86_64 native library are pinned together to
Valve OpenVR SDK 2.12.14, upstream commit
`91825305130f446f82054c1ec3d416321ace0072`. The manifest records the origin URL,
upstream Git blob identity, exact byte count and SHA-256 for every upstream file.
Git preserves those files byte-for-byte, including line endings.

Run `python tools/dependencies/verify_openvr.py` for an offline byte/provenance
and PE architecture audit. The audit never loads the DLL. Unity imports it only
for the Windows x64 player and Windows x64 Editor; preloading is disabled.
`Valve.OpenVR` is a separate assembly without automatic references. Synthetic
tracking has no dependency on it and never calls native initialization.

The upstream binding conditionally includes Unity convenience helpers, so its
assembly requires the UnityEngine reference to compile unmodified. ELTS adapters
must not call the binding's matrix `GetPosition`/`GetRotation` helpers: they apply
their own conversion and can return identity on invalid input. Use raw native
fields and the single validated `TrackingCoordinateConverter.Convert` boundary.
This keeps invalid observations explicit and avoids double handedness conversion.

Keep `LICENSE`, `NOTICE` and the dependency manifest with redistributed builds.
Change the binding and native library together, using one upstream revision,
then rerun the audit and Unity compilation. Never replace only one binary or
claim ABI/hardware acceptance from a successful checksum check.

Actual OpenVR background initialization, device enumeration, serial binding,
polling/reconnect behavior and physical validation are separate tracking tasks.
The currently unavailable testing equipment prevents their physical acceptance;
this vendoring step does not pass a study gate.
