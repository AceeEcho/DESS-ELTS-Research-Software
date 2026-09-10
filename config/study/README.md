# Study configuration boundary

Current staging accepts only explicit synthetic configuration.
`config/staging.json` selects the development scenario and synthetic rig template;
staging fails closed if the rig's `synthetic_only` calibration record is absent or
inconsistent. Physical study use requires a separate approved configuration,
measured calibration and the validation described in
[hardware follow-up](../../docs/operator/hardware-follow-up.md).
