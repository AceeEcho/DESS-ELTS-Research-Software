# Quaternius mannequin

The development targets use the rigged mannequin from [Universal Animation Library (Standard)](https://quaternius.com/packs/universalanimationlibrary.html) by Quaternius, released March 2025. Downloaded from the author's [itch.io page](https://quaternius.itch.io/universal-animation-library) on 2026-10-05.

CC0 1.0 permits use, modification and redistribution, including commercial use. The exact upstream notice and README are retained here; `provenance.json` records the archive and unchanged Unity FBX checksums.

The model is in `unity/Assets/ELTS/Operator/Resources/ELTS/Mannequin/UAL1_Standard.fbx`. Its scoped importer retains idle, walking, jogging, crouched idle and crouched walking, with no root motion. Runtime materials give it an ivory shell and charcoal joints. Runtime fitting uses the existing synthetic hit-region height and floor. The scenario still controls location/crouch and owns hit scoring; the visible mesh has no shot colliders.

This is a development visual asset, not evidence of physical validation.
