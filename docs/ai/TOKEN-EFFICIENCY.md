# Astra efficiency setup

Researched 2026-09-05. This is an additive agent-workflow implementation, not a
revision to the immutable architecture/workbook baselines or the progress schema.
No agent runtime or progress schemas existed in this workspace before this change.

## Settings and activation

Edit `config/ai/agent-policy.json`, then run from the project root:

```powershell
pwsh -File tools/ai/sync-config.ps1 -Write
pwsh -File tools/ai/sync-config.ps1
```

Requires PowerShell 7.4+ on the destination machine. The script validates the JSON
Schema, rejects unknown fields, and generates `.codex/config.toml`. It resolves
paths from its own location, so it also works when invoked from another directory.
Copy the whole setup, including the hidden `.codex` directory, when migrating.
Version 3 permits Spark, Luna, and Terra worker defaults. Versions 1 and 2 remain
historical schemas; the generator requires v3. To migrate v2, preserve scalar and
concurrency settings, change `$schema` to the v3 path and `schemaVersion` to 3,
and set `agents.default_subagent_model` to `gpt-5.3-codex-spark`. Then regenerate.
For v1, also add the required `agents` object from the current policy.

Defaults are Astra, medium reasoning effort, medium verbosity, and 4,000 retained tokens
per tool output. The output budget is a project starting point to evaluate, not an
OpenAI optimum. Increase it when necessary to avoid losing relevant diagnostics.
Schema guardrails of 1,000–16,000 are local policy bounds, not model limits.

Project configuration loads only for trusted projects. Explicit session/CLI choices
can override it; this file does not retroactively change the current task. Check
the model and effort selector when beginning work. For a CLI one-off, use
`codex -c 'model_reasoning_effort="medium"'`.
Project defaults and precedence are described in [Config basics](https://learn.chatgpt.com/docs/config-file/config-basic).

## Findings applied

- Astra's guide calls out excessive verification, sensitivity to instruction files,
  and response style. `AGENTS.md` provides bounded verification, concise reporting,
  and explicit delegation rules. It preserves required checks and task completion.
  [Astra guidance](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-6-astra)
- Use direct objectives and acceptance criteria; avoid requests to narrate internal
  reasoning. [Reasoning best practices](https://developers.openai.com/api/docs/guides/reasoning-best-practices)
- Codex exposes effort, verbosity, and retained tool-output controls. The local
  Astra catalog confirms verbosity support and a medium reasoning default; this
  setup chooses medium by default. Use low for routine tasks,
  high for difficult diagnosis or critical review, and evaluate quality before
  standardizing a higher level. [Configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference)
- Keep always-loaded instructions small; route task context through the index.
  Do not reduce the instruction-file byte cap to save tokens: that can omit rules.
  [AGENTS.md discovery](https://learn.chatgpt.com/docs/agent-configuration/agents-md)

The installed CLI is 0.153.4. Its cached Astra catalog reports a 272,000-token
context window, whereas the [API model page](https://developers.openai.com/api/docs/models/gpt-6-astra)
advertises 1,050,000. Leave context and compaction settings to the host; do not copy
API capacity into Codex configuration. The API excludes `none` effort. This schema
uses low through xhigh, the verified common subset; it does not expose app-only
ultra or assume every CLI supports max.

## Subagent routing (2026-09-06)

The user amended routing on 2026-09-06:

- `gpt-5.3-codex-spark`: default for small targeted fixes, focused tests, and bounded tasks.
- `gpt-5.6-luna`: higher-priority or moderately complex work.
- `gpt-5.6-terra`: more complex or consequential work as needed.

The policy retains medium default effort and seven spawned workers maximum, plus
one Astra coordinator (eight total across the session). This is a ceiling, not a
utilization target. Explicit dispatch selects Luna/Terra when the task warrants it;
there is no automatic complexity classifier in the TOML generator. `AGENTS.md`
defines the routing judgment, availability handling, and parent-fork constraints.

A configured model name does not guarantee host support. This amendment session's
collaboration tool lists Luna and Terra but not Spark; no Spark dispatch was
performed or claimed. A future host must expose Spark before using it. Report
unavailability and keep work local or explain a justified Luna/Terra escalation.
Do not create separate user-owned tasks to bypass subagent limitations.

[Official subagent configuration](https://learn.chatgpt.com/docs/agent-configuration/subagents)
documents that explicit spawn values override the configured defaults.
Inferred delegation opportunities are independent test suites on a fixed snapshot,
focused diagnosis of separate failures, fixes in disjoint modules, and independent
review. Each worker receives bounded context and returns evidence. Shared contract
changes, integration decisions, and final verification stay with Astra. Architecture
work must still obey baseline branch/worktree ownership and gate requirements.
This workspace has the PC-001 approved planning catalog but no runtime progress
reducer yet; bootstrap remains incomplete. No workbook task is completed by this
configuration change. Begin implementation through `docs/ai/START-HERE.md`.

Start a fresh project task after changing configuration; if the desktop host retains
old settings, restart Codex. A running task's tool limits are not changed by editing
TOML. Host limits and explicit session settings may take precedence. Validation of
files does not prove that eight agents have actually run concurrently.

## API considerations for a future agent runner

Keep stable instructions and tool definitions ahead of changing task context.
Cache reuse requires matching prefixes; it reduces processing cost, not context
size. Astra supports cache-preserving reasoning changes with `configuration_update`.
Compaction can reduce input length while losing cache reuse; measure total cost.
These are runner capabilities, not extra TOML keys implemented here.
[Prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching)

No API runner exists here, so there is no request schema to modify. Avoid treating
short visible answers or reasoning summaries as evidence of low reasoning usage.
Likewise, tool-history limits are not total task-token limits.

## Measure before claiming savings

Use the same small representative tasks and acceptance checks at low and medium:
one simulation edit, one multi-file fix, and one contract review. Compare successful
completion, regressions, retries, wall time, and total input/output tokens including
reasoning; record cached input separately. Include subagent use if applicable.
Use usage counters the host actually exposes and mark unavailable metrics unknown.
API dollar pricing does not establish subscription quota consumption.

No before/after model benchmark was run in this setup change. Configuration and
workflow improvements are implemented; token or cost savings remain unmeasured.

Setup validation passed: policy schema validation; rejection of unknown fields,
`none` effort, zero/fractional output limits, and an unsupported schema version;
independent TOML parsing and equality with the policy; regeneration drift check;
and invocation from the simulation subdirectory. These checks validate the files,
not the effective settings of an already-running desktop task.

Routing amendment validation (2026-09-06): v3 policy validation and regenerated
TOML drift check passed. The schema accepted Spark/Luna/Terra and rejected an
unsupported model and eight spawned workers. Independent Python TOML parsing
matched every emitted policy value. No worker dispatch or model benchmark was run.
