# Multi-machine development setup maintenance

- Request: user authorized configuring clone/setup/edit/push/pull across machines,
  publishing the result, and a beginner-friendly illustrated repository guide.
- Owner: Astra; branch `codex/multi-machine-setup`, based on `e3e85e5`.
- Scope: Windows development installer, local tool discovery, setup regression
  checks/CI, and documentation. Preserve existing uncommitted simulation edits.
- Delegation: Luna owns the new guide and illustrations on `codex/setup-guide`
  in an isolated worktree; coordinator owns integration and verification.
- Acceptance: official checksum-verified downloads, exact Unity/toolchain pins,
  repeatable setup without replacing local rig configuration, truthful failure
  reporting, illustrated clone/edit/synchronize instructions, verified publication.
- Checks: plan/catalog/progress/reducer; installer tests and PowerShell parsing;
  available bootstrap/runtime checks; documentation links and rendered images;
  clean Git clone and branch publication checks.
- This request is maintenance after accepted DEV-01 through DEV-12, not execution
  of `P0.2.S001` or a new study decision. Plan and progress validation passed with
  173 events, G0 pending, no active atomic owners. Do not reopen completed DEV steps
  or invent events for this out-of-catalog user request. Retain maintenance evidence
  here and in the linked verification note; authoritative study state is unchanged.
- Physical apparatus is built; testing equipment and second-machine verification
  remain pending. Installer simulations cannot establish a fresh-machine pass.

## Verified maintenance checkpoint

- Added `SETUP-DEV.cmd`, `OPEN-DEV-SHELL.cmd`, checksum-pinned tool installation,
  local discovery/cache recovery, .NET SDK selection, CI coverage, and the
  [illustrated operator guide](../operator/multi-machine-setup.md).
- Baseline: 82 Python tests and plan/progress/dependency checks passed. State still
  has 173 events; no study progress events or approvals were added.
- Installer: 41 offline regression checks passed under Windows PowerShell 5.1.
  They execute actual cache/hash validation, process exit/argv handling, and
  first/repeated local-cache publication with mocked network/install operations.
- Live setup: installed Node 24.21.0, reused Python 3.14.3, .NET 10.0.400 and Unity
  6000.3.23f1, preserved existing local configuration, imported the project, and
  passed all 29 verification check groups (including 3 EditMode / 5 PlayMode tests).
- Final local-cache implementation rerun: bootstrap/import passed with
  `-SkipVerification`; it correctly reported `prepared-unverified` instead of
  claiming a new full verification. The prior full test report remains separate.
- Five guide illustrations were rendered and inspected. The two-clone local Git
  rehearsal passed push/pull in both directions with matching content and HEAD.
- Luna's independent read-only review found a portable PowerShell path edge case;
  the coordinator replaced the `$PSHOME` assumption with executable discovery.
- Local diagnostics: `diagnostics/setup-helper-tests.log`, `setup-baseline.log`,
  `setup-git-sync.log`, `setup-report.json`, `test-report.json`, and fresh XML under
  `test-results/`. These are development checks, not physical or study acceptance.
- Existing uncommitted `elts-simulation/` edits were preserved and are outside
  this maintenance commit. The committed simulation source is included in Git.
- Clean GitHub checkout at `192df45218a884a67e6d723f821ccd041622f615`:
  setup created local configuration, rebuilt the Unity cache, and reported `ready`;
  all 29 check groups passed, including 3 EditMode and 5 PlayMode tests. This was
  a separate clone in a path containing spaces on the same Windows computer,
  with existing tools reused. Git object transfer used a dissociated local
  reference; the resulting clone has no alternates dependency. Evidence is under
  `build/Clean Clone With Spaces/diagnostics/` and `test-results/`.
- GitHub Actions run `34378196043` passed on Windows and Linux for that revision.
  The installer test harness now explicitly exits zero after passing checks,
  preventing an intentional negative process probe from leaking its exit code.
- Final launcher correction: prepend the Windows PowerShell module directory
  when starting from PowerShell 7. Actual `SETUP-DEV.cmd -Check` passed; this fixes
  inherited module discovery without changing the verified setup implementation.
