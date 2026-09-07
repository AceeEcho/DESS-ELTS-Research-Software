# Bootstrap progress runtime review

**Review target:** `b3e46abf1ae61b7878dc5fde827046d590f5496f`  
**Reviewer branch:** `review/bootstrap-progress`  
**Scope:** `tools/progress` reducer, writer, schemas, importer, and their
isolated fixtures. This is a correctness review; no production event or state
file was created or changed.

## Verdict: request changes

The reducer has good foundational protections: it rejects divergent task-chain
revisions, validates predecessor hashes, prevents state publication from
overwriting an existing event, binds evidence to a repository file checksum,
and regenerates stale state after a post-event publication crash. Those
protections are not sufficient to approve the runtime while privileged human
approval and sequential gate causality can be bypassed.

## Findings

| Priority | Location | Finding |
| --- | --- | --- |
| P0 | `tools/progress/core.py:241-262`; `tools/progress/build_schemas.py:108-110` | An arbitrary repository writer can self-enrol as a human approver and pass a gate. `check_approval` trusts the mutable `authorities.json` entry and a self-authored approval record; neither has an independently authenticated enrollment or approval root. This conflicts with `docs/modules/progress.md:25-28` and `:46-50`, and PC-001's requirement for explicit evidence-backed human approval. |
| P1 | `tools/progress/core.py:402-426` | A locked gate is treated as open once the preceding gate happened to be reduced first, but the new gate event is not required to have the preceding passed-gate event in its causal ancestry. A handcrafted G1 event with no causal parents can be accepted after a favorable digest tie-breaker. This violates the stated rule that causal hashes, rather than incidental reduction order, order dependencies. |
| P1 | `tools/progress/core.py:313-359` | A non-owner can alter an unowned sibling step in an already owned task. After the owner completed `P0.3.S001`, an unrelated actor successfully appended a `blocked` event to `BOOT.S002`; the task record still reported the original owner. The check protects only `record["owner"]`, which is unset for the next step, and invokes task ownership only from `check_claim` for `in_progress`. This can inject blockers and advance the shared task chain without a handoff. |
| P1 (validation portability) | `tools/progress/store.py:20-28`; `tools/progress/test_core.py:22-29` | The writer tests fail in this Windows checkout when `task-catalog.json` is materialized as CRLF, because `load_catalog` byte-compares it with an LF-only `json_bytes(expected)`. The fixture copies that checkout byte-for-byte. This did not indicate catalog/source tampering: the accepted-plan and amendment hashes matched the catalog. Re-exporting only the temporary fixture made all core tests pass. Do not regenerate the committed catalog merely to mask this checkout condition. |

### P0 reproduction: self-enrolled gate approval

In a temporary fixture root, the following actions were accepted by the b3
reducer:

1. Create `approval.json` with the expected target, actor, decision and
   evidence checksum fields.
2. Replace the fixture-only `authorities.json` with an entry naming that same
   actor, `G0`, and the checksum of `approval.json`.
3. Attach the existing fixture file to every G0 criterion while declaring its
   class `physical_test`.
4. Submit `gate_verification_pending`, then `gate_passed`, as that actor.

The generated state reported `gates.G0.status == "passed"`. The runtime did
not verify that the human identity, enrollment, approval, or asserted evidence
class came from an authority outside the repository writer's control. A local
hash only proves that the writer's own files have not changed since they were
referenced.

**Required correction:** production reduction must fail closed for all
privileged gate/decision/physical-human acceptance transitions until an
independently authenticated approval verifier is configured. Fixture verifiers
must be test-only and unavailable through the production CLI. Keeping the
registry as unauthorised candidate data is acceptable for bootstrap, provided
the state remains unable to present a gate or final decision as approved.

### P1 reproduction: gate causal bypass

Using an isolated fixture, G0 was first moved to `passed`. I created a G1
`gate_verification_pending` event, then removed its
`causalEventHashes`. Varying only its UUID until the deterministic digest
tie-breaker placed the G0 chain first allowed `reduce_state` to report
`gates.G1.status == "verification_pending"`.

**Required correction:** whenever a gate's locked-to-open rule relies on
`G(n-1)` being passed, require its last passed-event hash to occur in the new
event's causal ancestry. Add a regression fixture that supplies the same
causally unrelated event in several digest orders and rejects each one.

### P1 reproduction: sibling-step ownership bypass

Using the existing isolated `History` fixture:

1. Complete `P0.3.S001` as `fixture-agent`, leaving task `P0.3` owned by that
   actor.
2. Submit `blocked -> blocked` for ready `BOOT.S002` as `other-agent`, with a
   structurally valid blocker.

The reducer accepted the event and returned `BOOT.S002 == "blocked"` while
`P0.3.owner == "agent:fixture-agent"`. The bypass has no handoff and changes
the same task's revision chain.

**Required correction:** for every atomic-step transition, an existing task
owner/branch must match the event actor/branch unless a prior validated handoff
changed the task ownership. Preserve the existing per-step owner check as the
more specific constraint. Add the above regression case, including `ready`,
`blocked`, and `unblocked` transitions for an unowned sibling.

## Validation performed

- `python tools/progress/build_schemas.py --check` passed with the bundled
  Python runtime.
- The normal discovery command ran 24 tests. Fourteen schema/import tests
  passed; two writer tests failed only at the catalog CRLF precondition before
  exercising writer behavior.
- With an isolated temporary fixture re-exported by `export_outputs`, all 14
  `test_core` tests passed, including crash-recovery and no-overwrite cases.
- The two authorization/causality reproductions and the sibling ownership
  reproduction above used isolated temporary roots only.

## Material review limits

Evidence metadata is declared inside events and only the referenced repository
file's hash is machine-checked. The runtime therefore cannot establish that an
external lab result occurred or that an agent's declared `physical_test` class
is true. That boundary is consistent with the module's stated limitation only
after privileged approvals fail closed; it must not be described as automatic
verification of external evidence.

