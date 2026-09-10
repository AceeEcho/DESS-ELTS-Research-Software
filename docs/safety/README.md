# Safety and physical validation

The ELTS apparatus is built. Testing equipment is unavailable. Current software
uses synthetic tracking and simulated device links; passing those checks does
not establish physical calibration, timing, safety or study readiness.

Unity sends lifecycle commands only. It must never send head/weapon poses, world
targets, turret angles or individual LED commands to the apparatus. Normally closed
physical E-stop authority and device-owned safeguards remain independent of Unity.
Development software does not authorize energizing or modifying the built hardware.

Study configuration, approved research parameters, exposure limits and physical
acceptance require the responsible research/safety personnel and actual evidence.
Development constants and synthetic calibration are not substitutes. Existing study
entry points fail closed; removing repository management does not enable them.

New or repositioned parts require rig identity, tracker binding, calibration,
registration and physical validation. Preserve raw recordings and configuration
provenance. File hashes establish file integrity, not the truth of a lab observation.
See [required follow-up](../operator/hardware-follow-up.md) for outstanding work.
