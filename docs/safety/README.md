# Safety and physical validation

The ELTS apparatus is built. Testing equipment is unavailable. Software development
uses synthetic tracking and simulated device links, and cannot pass physical criteria.
The original specification and accepted PC-001 amendment remain authoritative.

Unity sends lifecycle commands only. It must never send head/weapon poses, world
targets, turret angles, or individual LED commands to the apparatus. Normally closed
physical E-stop authority and device-owned safeguards remain independent of Unity.
No development implementation authorizes energizing or modifying the built hardware.

All G0–G8 gates and physical evidence remain pending until their exact criteria and
explicit human approvals are satisfied. The progress CLI currently rejects privileged
approvals until an authenticated approval provider is integrated. Repository hashes
prove retained-file integrity, not the truth of an external lab observation.

New or repositioned parts require the applicable rig identity, binding, calibration,
registration, and physical validation. Synthetic geometry is unmeasured and cannot
be used as a silent study fallback. Follow the baseline specification for safety
limits; open D-01–D-18 decisions are not settled by development constants.
