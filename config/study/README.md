# Study configuration boundary

No study or measured calibration file is accepted in the BOOT.S017 synthetic
lane. `config/staging.json` intentionally selects the development scenario and
the synthetic rig template; staging fails closed if the rig's explicit
`synthetic_only` calibration record is absent or inconsistent. A future study
configuration requires its own approved schema and gate decision.
