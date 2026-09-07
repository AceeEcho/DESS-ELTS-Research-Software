# Repository controls and mirror reconciliation

Status: **documented, not enabled or verified remotely**. The origin is
`https://github.com/AceeEcho/DESS-ELTS-Research-Software`; read-only `git ls-remote
origin` returned no refs during BOOT.S025. PC-001 explicitly permits this documented
route for bootstrap. No push, merge, repository-setting update, Trello automation
or remote CI run is claimed.

The repository owner is the initial CODEOWNERS route. The owner must appoint the
appropriate research and safety reviewers before changes requiring those roles
can receive their approval. A CODEOWNERS entry alone does not prove review or
replace a scientific/safety acceptance criterion.

## Required main-branch configuration

Use `main` as the permanent integration branch. Before normal remote integration,
an authorized maintainer must configure and test an effective ruleset requiring:

- Pull requests with required CODEOWNER approval and applicable research/safety
  review; agents cannot approve or merge their own work.
- Passing repository policy/Python checks, with exact names captured from actual
  remote runs. The local workflow job is `Repository policy and Python baseline`
  for Windows and Ubuntu. Add Unity checks when the licensed runner is available
  and those checks have been observed; the current Unity workflow is manual.
- Resolved review conversations and linear history.
- No force pushes and no deletion of `main`.

Use squash merge by default and automatic deletion of merged branches. Do not
silently enable signed-commit enforcement: the team has not recorded a supported
signing workflow. Record that decision before enforcing it. Feature availability
depends on the actual GitHub account/repository plan; document a verified
equivalent or a remaining gap if the intended ruleset cannot be enforced.

Capture effective settings, check names, reviewer roles, actor/date and a test PR
showing that missing required checks or review prevent merging. This is a pending
remote control, separate from the local bootstrap completion evidence.

## Coordination mirrors

Repository plan definitions, events, evidence and decisions are canonical.
GitHub Issues and Trello may mirror stable task/step IDs, status and links. Before
updating a mirror, validate `PROJECT_STATE.json` against the reducer, then compare
each mirrored ID with the current generated status and evidence. Record missing,
stale or conflicting mirror values as drift. Never import a mirror's completion
state into repository events or automatically overwrite repository truth.

An authorized reconciliation records the before/after mirror values, canonical
event-set hash, changed links and unresolved drift. Sending updates externally
requires the user's authorization. Trello Butler behavior remains unverified;
do not rely on an untested rule to authorize work or pass gates.

## Moving work between machines

Use GitHub as the rendezvous point when authorized: push the task branch, record
the tested revision and exact next step, stop work on the old machine, then pull
on the new machine. Do not edit one task branch concurrently on both machines or
copy an active checkout as a substitute for synchronization. Separate worktrees
and claimed paths keep parallel work independent.

References: [PC-001](../plan/PC-001-approved-build-amendment.md),
[CI](../modules/ci.md), and
[GitHub rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets).
