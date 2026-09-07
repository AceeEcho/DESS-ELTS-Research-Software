import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

// Adjustable build settings are kept together so the workbook can be regenerated elsewhere.
const CONFIG = {
  sourcePlan: "C:/Users/cadet/AppData/Local/Temp/codex-file-preview-6uX6fS/ELTS-build-plan.md",
  outputDir: "C:/Users/cadet/.codex/.chatgpt-projects/g-p-6a9b09ad8e98819198f419d0f757f5da/outputs/01a06ea0-f0b2-79b2-8cdf-4c5d53f34938",
  outputFile: "ELTS_Implementation_and_Progress_Workbook.xlsx",
  specificationFile: "ELTS_Repository_Portability_and_Multi_LLM_Architecture_Specification.docx",
  repositoryUrl: "https://github.com/AceeEcho/DESS-ELTS-Research-Software",
  planVersion: "ELTS-build-plan-1.0",
  baselineDate: "2026-09-04",
  sourcePlanSha256: "2EDA196256668AC23D5F80497ACB1A040BA8E28E1AA53FB6195C6AF75B71F01F",
  font: "Arial",
  colors: {
    navy: "#17365D",
    blue: "#DCE6F1",
    paleBlue: "#EEF4FB",
    paleGray: "#F2F2F2",
    gray: "#666666",
    border: "#D9D9D9",
    white: "#FFFFFF",
    black: "#1F1F1F",
    green: "#E2F0D9",
    amber: "#FFF2CC",
    red: "#FCE4D6",
    purple: "#E4DFEC"
  }
};

const STATUS = ["not_started", "ready", "in_progress", "blocked", "verification_pending", "done", "superseded"];
const DECISION_STATUS = ["open", "in_review", "decided", "superseded", "informational"];
const GATE_STATUS = ["locked", "open", "verification_pending", "passed", "failed"];
const CRITERION_STATUS = ["not_evaluated", "pass", "fail", "blocked", "not_applicable"];
const EXECUTION_CLASS = ["agent", "human", "mixed", "lab_hardware", "automated_check"];
const REQUIREMENT_LEVEL = ["MUST", "MUST NOT", "SHOULD", "SHOULD NOT", "MAY"];

function stripMd(value) {
  return String(value ?? "")
    .replace(/<[^>]+>/g, "")
    .replace(/\*\*/g, "")
    .replace(/\*/g, "")
    .replace(/`/g, "")
    .replace(/\[(.*?)\]\([^)]*\)/g, "$1")
    .replace(/\s+/g, " ")
    .trim();
}

function phaseMeta(phase) {
  const map = {
    P0: { name: "Foundations", gate: "G0", entry: "Project start", dependency: "", lane: "Foundation" },
    P1: { name: "Tracking online", gate: "G1", entry: "G0 passed and tracking hardware available", dependency: "G0", lane: "Tracking and fabrication" },
    P2: { name: "Core services", gate: "G2", entry: "G1 passed, or G0 with synthetic tracking", dependency: "G1 or (G0 and P2.9)", lane: "Core software" },
    P3: { name: "Rendering", gate: "G3", entry: "G2 passed, P1.14 complete, any display available", dependency: "G2, P1.14", lane: "Unity rendering" },
    P4: { name: "Scenario and session", gate: "G4", entry: "G3 passed", dependency: "G3", lane: "Study application" },
    P5: { name: "Calibration", gate: "G5", entry: "G4 passed, mock weapon and pivot probe complete", dependency: "G4, P1.13, P5.1 prerequisites", lane: "Calibration and fabrication" },
    P6: { name: "ELTS integration", gate: "G6", entry: "G4 passed, D-03 and D-10 decided, ELTS owner available", dependency: "G4, D-03, D-10", lane: "Unity and Jetson integration" },
    P7: { name: "Bench validation and safety", gate: "G7", entry: "G5 and G6 passed, D-01 and D-02 implemented", dependency: "G5, G6, D-01, D-02", lane: "Lab validation" },
    P8: { name: "Pilot and freeze", gate: "G8", entry: "G7 passed and all decisions closed except D-15", dependency: "G7, D-01:D-18 except D-15", lane: "Study operations and release" }
  };
  return map[phase];
}

function workstreamFor(id) {
  const [p, nText] = id.split(".");
  const n = Number(nText);
  if (p === "P0") return ({1:"WS-A, WS-F",2:"WS-C, WS-F",3:"WS-C",4:"WS-F",5:"WS-F",6:"WS-E, WS-F",7:"WS-E, WS-F"})[n] ?? "WS-F";
  if (p === "P1") return n <= 3 ? "WS-A" : n <= 10 ? "WS-A, WS-C" : "WS-B";
  if (p === "P2") return n === 3 ? "WS-A, WS-C" : n === 8 ? "WS-C, WS-F" : "WS-C";
  if (p === "P3") return n <= 3 || n === 6 ? "WS-C, WS-E" : "WS-C";
  if (p === "P4") return n >= 8 ? "WS-C, WS-F" : "WS-C";
  if (p === "P5") return n === 1 ? "WS-B, WS-C" : "WS-C, WS-E";
  if (p === "P6") return n >= 7 && n <= 10 ? "WS-D" : "WS-C, WS-D";
  if (p === "P7") return n === 8 ? "WS-E, WS-F" : "WS-C, WS-D, WS-E";
  if (p === "P8") return n === 7 ? "WS-C, WS-F" : "WS-E, WS-F";
  return "";
}

function ownerFor(id) {
  const [p, nText] = id.split(".");
  const n = Number(nText);
  if (p === "P0" && [1,5,6,7].includes(n)) return "Investigator and developer";
  if (p === "P1" && n >= 11) return "Fabricator and investigator";
  if (p === "P5" && n === 1) return "Fabricator and developer";
  if (p === "P6" && n >= 7 && n <= 10) return "ELTS code owner";
  if (p === "P7" || p === "P8") return "Investigator, developer, and lab operator";
  return "Developer";
}

function executionClassFor(id) {
  const [p, nText] = id.split(".");
  const n = Number(nText);
  if (p === "P0" && [1,2,5,6,7].includes(n)) return "mixed";
  if (p === "P1" && [1,2,3,8,9,10,11,12,13,14].includes(n)) return "lab_hardware";
  if (p === "P5") return n === 2 ? "agent" : "lab_hardware";
  if (p === "P6" && [8,11,12,13].includes(n)) return "mixed";
  if (p === "P7") return "lab_hardware";
  if (p === "P8" && [1,2,3,4,5,8].includes(n)) return "mixed";
  return "agent";
}

const directDependencies = {
  "P1.2":"P1.1", "P1.3":"P1.2", "P1.4":"P1.2", "P1.5":"P1.3, P1.4", "P1.6":"P1.4", "P1.7":"P1.4", "P1.8":"P1.3, P1.4", "P1.9":"P1.4", "P1.10":"P0.2", "P1.11":"D-09", "P1.12":"P1.11", "P1.13":"P1.12", "P1.14":"P1.13",
  "P2.2":"P2.1", "P2.3":"P1.5, P1.6, P1.7, P1.8, P1.9, P2.1, P2.2", "P2.4":"P2.1", "P2.5":"P2.2, P2.3", "P2.6":"P2.2, P2.5", "P2.7":"P2.2, P2.4", "P2.8":"P2.5, P2.6", "P2.9":"P2.1, P2.2",
  "P3.2":"P2.3, P2.4, P3.1", "P3.3":"P3.2, P1.13", "P3.4":"P2.3, P2.4, P3.1", "P3.5":"P1.10", "P3.6":"P3.2, P2.5", "P3.7":"P3.2, P3.4", "P3.8":"P2.5, P3.4",
  "P4.2":"P4.1, P2.7", "P4.3":"P4.1, D-04", "P4.4":"P4.1, P2.4", "P4.5":"P2.3, P2.4, P4.1, D-05", "P4.6":"P2.6, P4.5", "P4.7":"P4.1, D-06", "P4.8":"D-07", "P4.9":"P4.8, D-08", "P4.10":"P4.8, P4.9", "P4.11":"P4.10", "P4.12":"P2.1, P2.3, P2.5, P3.5", "P4.13":"P4.1:P4.12",
  "P5.1":"P1.11, P1.12, P1.13", "P5.3":"P5.2, P1.13", "P5.4":"P5.2, P5.3", "P5.5":"P5.2", "P5.6":"P5.2, P5.5", "P5.7":"P5.1, P5.2, P5.4", "P5.8":"P5.4, P5.7", "P5.9":"P5.1, P5.4", "P5.10":"P4.10, P5.5, P5.6, P5.7, P5.8",
  "P6.2":"P6.1, D-03", "P6.3":"P2.2, P6.1, P6.2", "P6.4":"P6.1, P6.2", "P6.5":"P4.10, P6.1:P6.4, D-10", "P6.6":"P6.1", "P6.7":"P6.1, P6.2", "P6.8":"P6.7, D-13", "P6.9":"P6.7", "P6.10":"P6.7", "P6.11":"P6.1:P6.10", "P6.12":"P6.8", "P6.13":"P6.5, P6.7",
  "P7.1":"G5, G6, D-01, D-02", "P7.2":"P7.1", "P7.3":"P7.1, D-11", "P7.4":"P7.1", "P7.5":"P7.1", "P7.6":"P7.1:P7.5", "P7.7":"P7.1", "P7.8":"P6.8, D-13, D-14", "P7.9":"P7.2:P7.5, D-17",
  "P8.1":"G7, D-04, D-05, D-06, D-07, D-08, D-16", "P8.2":"G7", "P8.3":"G7", "P8.4":"D-12", "P8.5":"P8.1:P8.4", "P8.6":"P8.5", "P8.7":"P8.1:P8.6", "P8.8":"P8.2"
};

const artifactHints = {
  "P0.1":"docs/procurement/BOM.md", "P0.2":"docs/environment/study-pc.md", "P0.3":"repository skeleton and clean-clone evidence", "P0.4":"GitHub Issues or approved tracker export", "P0.5":"planning/decisions/", "P0.6":"docs/safety/", "P0.7":"docs/rig/room-survey.md",
  "P1.3":"config/rig/rig.json and tracker-role evidence", "P1.4":"docs/spikes.md", "P1.5":"docs/spikes.md", "P1.6":"docs/spikes.md and rate/jitter output", "P1.7":"docs/spikes.md", "P1.8":"docs/spikes.md", "P1.9":"docs/spikes.md", "P1.10":"docs/spikes.md",
  "P2.1":"config/, schemas/config/, tests", "P2.2":"unity core clock and policy check", "P2.3":"unity tracking service and tests", "P2.4":"pure C# geometry library and tests", "P2.5":"logging pipeline and soak evidence", "P2.6":"event types and tests", "P2.7":"snapshot channel and test", "P2.8":"analysis ingest package and fixtures", "P2.9":"synthetic tracking source and tests",
  "P3.1":"rig calibration development config", "P3.2":"off-axis camera and projection tests", "P3.3":"virtual room and corner-rod evidence", "P3.4":"operator view", "P3.5":"display manager and local config", "P3.6":"prediction decision record", "P3.7":"performance report", "P3.8":"replay module",
  "P4.1":"config/study/scenarios and schemas", "P4.8":"config/study/sessions", "P4.13":"dry-run data in secure non-repository storage plus summary",
  "P5.3":"calibration wizard and residual evidence", "P5.4":"calibration_rig output and rod evidence", "P5.8":"verification outputs", "P5.9":"rig_checks.csv outside Git plus procedure",
  "P6.1":"protocol schema and codec", "P6.2":"transport adapters", "P6.7":"Jetson service", "P6.8":"fail-safe evidence", "P6.11":"link test report", "P6.12":"fail-safe report",
  "P7.2":"docs/validation/D.1-infrared.md", "P7.3":"docs/validation/D.2-washout.md", "P7.4":"docs/validation/D.3-occlusion.md", "P7.5":"docs/validation/D.4-registration.md", "P7.6":"docs/validation/D.6-soak.md", "P7.8":"docs/safety/", "P7.9":"docs/thresholds.md",
  "P8.2":"docs/operator/", "P8.3":"docs/DATA-GOVERNANCE.md", "P8.4":"docs/validation/cross-device-sync.md", "P8.5":"pilot summaries", "P8.6":"analysis block-level output", "P8.7":"tag, release bundle, docs/frozen.md", "P8.8":"docs/operator/contingencies.md"
};

function parseTasks(markdown) {
  const phaseMatches = [...markdown.matchAll(/^## \d+\. Phase (\d+) — (.+)$/gm)];
  const tasks = [];
  for (let pIndex = 0; pIndex < phaseMatches.length; pIndex++) {
    const pm = phaseMatches[pIndex];
    const phase = `P${pm[1]}`;
    const start = pm.index;
    const end = pIndex + 1 < phaseMatches.length ? phaseMatches[pIndex + 1].index : markdown.indexOf("## 12.", start);
    const section = markdown.slice(start, end > start ? end : markdown.length);
    const matches = [...section.matchAll(/^- \*\*(P\d+\.\d+) — (.+?)(?:\.)?\*\*(.*)$/gm)];
    for (let i = 0; i < matches.length; i++) {
      const m = matches[i];
      const segStart = m.index;
      const segEnd = i + 1 < matches.length ? matches[i + 1].index : section.indexOf("### Gate", segStart) >= 0 ? section.indexOf("### Gate", segStart) : section.length;
      const block = section.slice(segStart, segEnd).trim();
      const doneMatch = block.match(/\*Done when:\*\s*([^\n]+)/i);
      const done = doneMatch ? stripMd(doneMatch[1]) : "Acceptance criteria and required evidence are satisfied and recorded.";
      const nested = [...block.matchAll(/^\s{2,}-\s+(.+)$/gm)].map(x => stripMd(x[1])).filter(Boolean);
      const firstRemainder = stripMd(m[3]);
      const following = block.split(/\r?\n/).slice(1)
        .filter(line => !/^\s{2,}-\s+/.test(line) && !/Done when/i.test(line) && !/^\s*$/.test(line))
        .map(stripMd).filter(Boolean);
      const detailParts = [firstRemainder, ...following, ...nested].filter(Boolean);
      const title = stripMd(m[2]).replace(/\.$/, "");
      const meta = phaseMeta(phase);
      const decisions = [...new Set(block.match(/D-\d{2}/g) ?? [])].join(", ");
      tasks.push({
        id: m[1], phase, phaseName: meta.name, gate: meta.gate, workstream: workstreamFor(m[1]),
        title, action: detailParts.join("\n"), entry: meta.entry,
        dependencies: directDependencies[m[1]] ?? meta.dependency,
        decisions, done, validation: done,
        artifacts: artifactHints[m[1]] ?? "Code, documentation, test evidence, or lab evidence named in the task",
        owner: ownerFor(m[1]), executionClass: executionClassFor(m[1]), lane: meta.lane,
        source: `ELTS Build Plan ${m[1]}`
      });
    }
  }
  return tasks;
}

function parseDecisions(markdown) {
  const section = markdown.slice(markdown.indexOf("## 15. Decision Register"), markdown.indexOf("## 16. Risk Register"));
  const rows = [];
  for (const line of section.split(/\r?\n/)) {
    if (!/^\| D-\d{2}/.test(line)) continue;
    const cells = line.split("|").slice(1, -1).map(c => stripMd(c.replace("★", "")));
    if (cells.length < 5) continue;
    rows.push({ id: cells[0], decision: cells[1], owner: cells[2], blocks: cells[3], criteria: cells[4], status: cells[0] === "D-15" ? "informational" : "open" });
  }
  return rows;
}

function parseGates(markdown) {
  const gates = [];
  for (let g = 0; g <= 7; g++) {
    const re = new RegExp(`### Gate G${g}[^\\n]*\\n([\\s\\S]*?)(?=\\n---|\\n## )`);
    const m = markdown.match(re);
    if (!m) continue;
    const criteria = m[1].split(/\r?\n/).filter(x => /^- \[ \]/.test(x)).map(x => stripMd(x.replace(/^- \[ \]\s*/, "")));
    criteria.forEach((criterion, i) => gates.push({ gate: `G${g}`, criterionId: `G${g}.${String(i + 1).padStart(2, "0")}`, criterion, source: `ELTS Build Plan Gate G${g}`, status: "not_evaluated" }));
  }
  const definition = markdown.slice(markdown.indexOf("### 1.3 Definition of done"), markdown.indexOf("### 1.4 Assumptions"));
  const items = [...definition.matchAll(/^\d+\.\s+(.+)$/gm)].map(x => stripMd(x[1]));
  items.forEach((criterion, i) => gates.push({ gate: "G8", criterionId: `G8.${String(i + 1).padStart(2, "0")}`, criterion, source: "ELTS Build Plan section 1.3", status: "not_evaluated" }));
  return gates;
}

function parseRisks(markdown) {
  const section = markdown.slice(markdown.indexOf("## 16. Risk Register"), markdown.indexOf("## Appendix A"));
  const rows = [];
  for (const line of section.split(/\r?\n/)) {
    if (!/^\| R-\d{2}/.test(line)) continue;
    const c = line.split("|").slice(1, -1).map(stripMd);
    rows.push({ id:c[0], risk:c[1], effect:c[2], mitigation:c[3], verifiedBy:c[4], owner:"Assigned during planning", status:"open" });
  }
  rows.push(
    {id:"R-22",risk:"Agent or workbook status diverges from repository state",effect:"The team acts on stale progress",mitigation:"Append-only progress events, deterministic reducer, CI regeneration check; JSON state overrides workbook status",verifiedBy:"STATE-003, STATE-004",owner:"Repository maintainer",status:"open"},
    {id:"R-23",risk:"Two agents edit the same task or files concurrently",effect:"Conflicts or inconsistent implementations",mitigation:"One owner and branch per task; declared path claims; separate worktrees; reducer rejects divergent task event chains",verifiedBy:"AI-003, AI-012",owner:"Repository maintainer",status:"open"},
    {id:"R-24",risk:"Toolchain or dependency drift across machines",effect:"Builds or analyses cannot be reproduced",mitigation:"Exact versions, lockfiles, doctor reports, clean-machine portability tests",verifiedBy:"BUILD-001, BUILD-002, POR-009",owner:"Developer",status:"open"},
    {id:"R-25",risk:"Secrets, hardware identifiers, or research data enter the public repository",effect:"Privacy, security, or compliance incident",mitigation:"Templates only, deny patterns, secret/data scans, human review, private deployment overlays where required",verifiedBy:"DATA-001, DATA-002, CFG-005",owner:"Investigator and maintainer",status:"open"},
    {id:"R-26",risk:"An AI-generated change alters a scientific invariant or safety control",effect:"Invalid study data or unsafe operation",mitigation:"Invariant tests, protected paths, human approval, gate re-verification",verifiedBy:"AI-005, AI-006, SAFE-003",owner:"Investigator",status:"open"}
  );
  return rows;
}

const p03Atomic = [
  ["Inventory the repository, remote, default branch, files, and existing uncommitted work without modifying anything.","Repository inventory recorded; existing work preserved."],
  ["Create the progress-state schemas, initial append-only progress event, reducer entry point, and generated PROJECT_STATE.json before other architecture changes.","Schemas validate and PROJECT_STATE.json names the exact current task."],
  ["Preserve unchanged copies of this workbook and the architecture specification under project-management/baseline/.","Both baseline files exist with original filenames."],
  ["Compute SHA-256 hashes for both baseline files and record them in the task catalog and generated state.","Hashes match the preserved files."],
  ["Export the workbook registries to diffable machine-readable files for work items, atomic steps, decisions, gate criteria, requirements, artifacts, and risks.","Exports validate and retain every stable ID."],
  ["Create the canonical monorepo directory skeleton without deleting or relocating existing user work.","Repository layout matches the approved specification or deviations have an ADR."],
  ["Create the root README with project purpose, safe entry points, quick start, repository map, and links to canonical documents.","A new contributor can find setup, state, tests, and architecture from README."],
  ["Create root AGENTS.md as the canonical model-agnostic instruction entry point.","AGENTS.md points to the instruction chain and progress state."],
  ["Create CLAUDE.md, .github/copilot-instructions.md, and future model adapters as thin pointers rather than duplicated policy.","Adapters contain no conflicting rules."],
  ["Create docs/ai/START-HERE.md, CHANGE-PROTOCOL.md, REVIEW-CHECKLIST.md, CONTEXT-INDEX.md, and a handoff template.","Instruction chain is complete and link-checked."],
  ["Create docs/INVARIANTS.md, ARCHITECTURE.md, PORTABILITY.md, DEVELOPMENT.md, GIT-WORKFLOW.md, TESTING.md, RELEASES.md, REPRODUCIBILITY.md, DATA-GOVERNANCE.md, SECURITY.md, and GLOSSARY.md.","Each canonical document exists with ownership and update triggers."],
  ["Create .gitignore rules for Unity-generated directories, builds, local configuration, secrets, participant calibration, research data, logs, and temporary files.","Policy scan proves prohibited paths are ignored; synthetic fixtures remain allowed."],
  ["Create .gitattributes and a documented Git LFS policy, plus .editorconfig for portable line endings and formatting.","Line-ending and binary handling are deterministic and documented."],
  ["Create versioned JSON Schemas for configuration, logs, ELTS protocol, task catalog, progress events, progress state, and diagnostic reports.","Schema validation passes on all examples."],
  ["Create portable entry scripts for developer bootstrap, doctor, verification, tests, build, packaging, and study-machine first run, with variables centralized at the top or in configuration.","Entry scripts support paths with spaces and emit precise failures."],
  ["Create the Unity 6 LTS project under unity/ and record the exact editor version.","ProjectVersion.txt and README agree."],
  ["Configure the Universal Render Pipeline and commit all required project settings and meta files.","Clean import succeeds with the pinned editor."],
  ["Remove Unity XR packages and add an automated check that rejects com.unity.xr packages.","Package manifest and lock contain no prohibited XR package."],
  ["Vendor the exact OpenVR bindings and native library with license, upstream version or commit, and checksums.","Third-party provenance is complete and verified."],
  ["Add pinned Newtonsoft JSON and Unity Test Framework dependencies.","Package manifest and lock agree."],
  ["Create EditMode and PlayMode test assemblies with one passing smoke test each.","Both suites run successfully in batch mode."],
  ["Create the versioned Windows standalone build script using an explicit build target or profile.","A batch build emits version and provenance metadata."],
  ["Create repository-policy and Unity CI workflows, initially advisory until their check names are established.","Workflows validate locally or in a pull request."],
  ["Configure the GitHub main-branch ruleset after CI checks exist: pull request, required checks, conversation resolution, linear history, no force pushes or deletion.","Ruleset settings are captured in repository documentation."],
  ["Run a clean-clone bootstrap, test, and standalone-build rehearsal in a path containing spaces.","All commands pass and the doctor report is preserved as evidence."],
  ["Regenerate PROJECT_STATE.json from events and confirm it reports the next eligible task and all blockers deterministically.","CI regeneration produces no diff."],
];

function classifyAction(text) {
  const s = text.toLowerCase();
  if (/decide|select|approve|sign off|close d-/.test(s)) return "decision";
  if (/test|verify|confirm|measure|audit|rehears|inspect|validate|check/.test(s)) return "verification";
  if (/document|record|write|readme|procedure|report|screenshot/.test(s)) return "documentation";
  if (/install|mount|wire|fabricat|print|build|create|implement|add|configure|develop|wrap|integrate/.test(s)) return "implementation";
  return "execution";
}

function atomicStepsFor(tasks) {
  const out = [];
  let seq = 1;
  for (const task of tasks) {
    if (task.id === "P0.3") {
      p03Atomic.forEach((pair, idx) => {
        const id = `P0.3.S${String(idx + 1).padStart(3, "0")}`;
        out.push({seq:seq++, id, parent:task.id, phase:task.phase, gate:task.gate, workstream:task.workstream, stepType:classifyAction(pair[0]), action:pair[0], dependencies:idx === 0 ? "" : `P0.3.S${String(idx).padStart(3, "0")}`, decisions:"", evidence:pair[1], validation:pair[1], artifacts:task.artifacts, status:idx === 0 ? "ready" : "not_started", owner:idx === 23 ? "Repository owner" : "Initial coding agent", executionClass:idx === 23 ? "human" : "agent", branch:"bootstrap/repository-governance", source:task.source});
      });
      continue;
    }
    const actionLines = task.action.split(/\n+/).map(stripMd).filter(Boolean);
    const unique = [...new Set(actionLines.length ? actionLines : [task.title])];
    unique.forEach((action, idx) => {
      const id = `${task.id}.S${String(idx + 1).padStart(3, "0")}`;
      out.push({seq:seq++, id, parent:task.id, phase:task.phase, gate:task.gate, workstream:task.workstream, stepType:classifyAction(action), action, dependencies:idx === 0 ? task.dependencies : `${task.id}.S${String(idx).padStart(3, "0")}`, decisions:task.decisions, evidence:idx === unique.length - 1 ? task.done : `Change or evidence attributable to ${id}`, validation:idx === unique.length - 1 ? task.validation : "Review the changed artifact and run the nearest relevant automated or lab check.", artifacts:task.artifacts, status:"not_started", owner:task.owner, executionClass:task.executionClass, branch:`${task.phase.toLowerCase()}/${task.id}-${task.title.toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-|-$/g,"").slice(0,45)}`, source:task.source});
    });
    const verifyId = `${task.id}.V001`;
    const lastId = `${task.id}.S${String(unique.length).padStart(3, "0")}`;
    out.push({seq:seq++, id:verifyId, parent:task.id, phase:task.phase, gate:task.gate, workstream:task.workstream, stepType:"verification", action:`Verify ${task.id} against its completion criteria and attach evidence.`, dependencies:lastId, decisions:task.decisions, evidence:task.done, validation:task.validation, artifacts:task.artifacts, status:"not_started", owner:task.owner, executionClass:task.executionClass === "agent" ? "automated_check" : task.executionClass, branch:`${task.phase.toLowerCase()}/${task.id}-${task.title.toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-|-$/g,"").slice(0,45)}`, source:task.source});
  }
  return out;
}

const requirements = [
  ["SCI-001","MUST","Continuous aim error is the world-space angular error in degrees between the zero-corrected bore ray and the muzzle-to-target ray.","P2.4, P2.7, P4.5","G2, G4, G8"],
  ["SCI-002","MUST","A target's canonical position is world-space; screen coordinates are derived each frame and never canonical target state.","P2.7, P4.2","G4"],
  ["SCI-003","MUST NOT","Unity sends head pose, target coordinates, turret angles, or individual LED commands to ELTS; only lifecycle commands are allowed.","P6.1, P6.5, P6.7","G6"],
  ["SCI-004","MUST","The study uses no HMD and nothing near the eyes beyond approved measurement equipment.","P1.2, P1.14","G1, G8"],
  ["SCI-005","MUST","All application timestamps derive from one monotonic clock started at application launch; UTC is only an alignment anchor.","P2.2, P6.3","G2, G6"],
  ["SCI-006","MUST","Every tracking sample contains connection and validity flags; invalid poses are excluded and never interpolated.","P2.3, P2.5, P2.8","G2, G8"],
  ["SCI-007","MUST","Trackers bind by configured serial number, never device index.","P1.3, P1.5, P2.3","G1, G8"],
  ["SCI-008","MUST","Logged tracking uses zero prediction; rendering-only head prediction is isolated and the weapon pose remains raw.","P1.6, P2.3, P3.6","G1, G3"],
  ["SCI-009","MUST","Trigger handling accepts the first falling edge and applies the configured lockout, nominally 20 ms.","P1.8, P2.3","G2"],
  ["SCI-010","MUST","The primary block DV remains the count of TargetDestroyed events during the fixed 300-second block.","P4.6, P4.8","G4, G8"],
  ["SCI-011","MUST","Scenario, behavior, scoring, session, geometry, display, and ELTS transport choices remain data-driven or interface-driven.","P2.1, P4.1:P4.8, P6.1:P6.2","G4, G6"],
  ["SCI-012","MUST","Participant and operator rendering remain independent; operator content never reaches the participant display.","P3.2, P3.4, P3.5","G3"],
  ["SAFE-001","MUST","Heartbeat timeout, maximum on-time, and normally closed physical E-stop operate as independent safeguards.","P6.8, P6.12","G6, G7"],
  ["SAFE-002","MUST NOT","Software overrides or weakens the physical E-stop.","P6.8, P6.12","G6"],
  ["SAFE-003","MUST","Safety-critical ELTS changes receive human review and repeat the applicable fail-safe and bench tests.","P6.8, P7.8","G6, G7"],
  ["SAFE-004","MUST","The LED exposure basis and operational limits are documented and agree with software guards.","P0.6, P7.8","G7"],
  ["SAFE-005","MUST","The mock weapon is inert, visibly marked, secured, and covered by campus notification procedures.","P0.6, P1.11:P1.13","G0, G8"],
  ["SAFE-006","MUST","A failed daily registration check stops participant operation until recalibration and incident recording are complete.","P5.9, P8.2","G5, G8"],
  ["POR-001","MUST NOT","Committed code or configuration contains user-specific absolute paths.","P0.3","repository-policy CI"],
  ["POR-002","MUST","Release scripts and runtime work from arbitrary writable extraction paths, including paths with spaces.","P0.3, P8.7","clean-path portability test"],
  ["POR-003","MUST","Bootstrap and first-run operations are idempotent.","P0.3","repeat-run test"],
  ["POR-004","MUST","Provisioning backs up existing local configuration or calibration before replacement.","P0.3, P5.4","provisioning test"],
  ["POR-005","MUST NOT","The study runtime requires the Unity Editor.","P0.3, P8.7","G8"],
  ["POR-006","SHOULD NOT","Prepared study operation requires internet connectivity.","P0.3, P8.7","offline rehearsal"],
  ["POR-007","MUST","Diagnostics emit both readable output and a versioned machine-readable report.","P0.3, P4.12","doctor schema test"],
  ["POR-008","MUST","A new physical rig receives a new rig identity and required recalibration instead of inheriting old calibration silently.","P5.4, P7.1","G5, G7"],
  ["POR-009","MUST","Unbundled prerequisites are precisely documented and detected.","P0.2, P0.3","doctor checks"],
  ["POR-010","MUST","Every study release is verified after copy and extraction, not only in the build directory.","P8.7","release smoke test"],
  ["CFG-001","MUST","Every configuration type has a versioned JSON Schema.","P0.3, P2.1","schema CI"],
  ["CFG-002","MUST","Unknown, missing, or mistyped required fields cause a precise startup failure.","P2.1","G2"],
  ["CFG-003","MUST","Every effective configuration file is hashed and recorded in logs and build provenance.","P2.1, P2.5, P8.7","G2, G8"],
  ["CFG-004","MUST","local.json is generated from a committed example and contains only whitelisted machine-local fields.","P0.3, P3.5","config validation"],
  ["CFG-005","MUST NOT","local configuration, participant calibration, participant data, credentials, or machine secrets are committed.","P0.3, P8.3","repository-policy CI"],
  ["CFG-006","MUST NOT","Study settings and machine-local settings are mixed in one layer.","P0.3, P2.1","schema review"],
  ["CFG-007","MUST","A schema change increments a version and provides a migration or declares incompatibility.","P2.1, P2.8","compatibility tests"],
  ["CFG-008","MUST","Configuration precedence is deterministic and documented.","P0.3, P2.1","configuration tests"],
  ["CFG-009","MUST","The operator can inspect effective configuration and hashes before a session.","P4.11, P4.12","G4"],
  ["CFG-010","MUST","A frozen study configuration change during data collection creates a new tag and study-log entry.","P8.7","release policy"],
  ["REP-001","MUST","The system uses one monorepo for Unity, Jetson, analysis, config, tooling, documentation, and governance.","P0.3","repository layout check"],
  ["REP-002","MUST","Root config is the sole hand-edited source; Unity StreamingAssets receives a generated staged copy.","P0.3, P2.1","config divergence CI"],
  ["REP-003","SHOULD NOT","Git submodules are used before study-v1.0.","P0.3","repository policy"],
  ["REP-004","MUST","Vendored OpenVR files include upstream version, license, and checksums.","P0.3","third-party audit"],
  ["REP-005","MUST","Unity ProjectSettings, package manifests, lockfiles, and all .meta files are committed.","P0.3","Unity policy check"],
  ["REP-006","MUST NOT","Unity Library, Temp, Logs, obj, Build, Builds, or UserSettings directories are committed.","P0.3","repository-policy CI"],
  ["REP-007","MUST","Binary baseline artifacts also receive diffable normalized exports for agents and review.","P0.3","ID and hash reconciliation"],
  ["REP-008","MUST","Repository paths and scripts use portable relative resolution from the repository or release root.","P0.3","path portability test"],
  ["BUILD-001","MUST","The exact Unity 6 LTS editor version is pinned and checked.","P0.3","doctor and build log"],
  ["BUILD-002","MUST","Unity and Python dependencies are locked with committed lockfiles.","P0.3, P2.8","lockfile CI"],
  ["BUILD-003","MUST","Study releases are built from a clean Git worktree.","P8.7","release workflow"],
  ["BUILD-004","MUST","Dirty development builds identify themselves and cannot be used for participant data collection.","P0.3, P8.7","build metadata check"],
  ["BUILD-005","MUST","The build records tag, commit, versions, schemas, configuration hashes, target, UTC, and artifact checksum.","P0.3, P8.7","build-info validation"],
  ["BUILD-006","MUST","The release contains FIRST-RUN, CHECK-SYSTEM, START-ELTS, operator docs, licenses, build-info, and MANIFEST.sha256.","P8.7","release layout test"],
  ["BUILD-007","MUST","CI specifies an explicit Unity build target or profile for reliable command-line builds.","P0.3","Unity CI"],
  ["BUILD-008","MUST","Reproducibility claims are functional and provenance-based unless bit-for-bit determinism is separately proven.","P0.3, P8.7","release documentation"],
  ["GIT-001","MUST","main represents coherent, tested, documented work.","P0.3","protected ruleset"],
  ["GIT-002","MUST","Normal changes reach main through a pull request and required checks.","P0.3","ruleset"],
  ["GIT-003","MUST","One task maps to one temporary branch and one active owner.","P0.3","progress validator"],
  ["GIT-004","SHOULD","Pull requests use squash merge and merged branches are deleted.","P0.3","repository settings"],
  ["GIT-005","MUST NOT","The same task branch is edited simultaneously on multiple machines.","P0.3","contribution policy"],
  ["GIT-006","MUST","A conflict is resolved deliberately and all affected checks are rerun.","P0.3","review checklist"],
  ["GIT-007","MUST NOT","Force pushes or deletion of main are allowed.","P0.3","ruleset"],
  ["GIT-008","MUST","Safety, invariants, frozen configuration, and study release tags receive human approval.","P6.8, P7.8, P8.7","protected review"],
  ["AI-001","MUST","An agent reads the canonical instruction chain before editing.","P0.3","handoff and PR checklist"],
  ["AI-002","MUST","An agent inspects PROJECT_STATE.json and its assigned atomic-step row before work.","P0.3","progress event validation"],
  ["AI-003","MUST","An agent declares task, branch, owner, scope, claimed paths, and stop conditions.","P0.3","task_started event"],
  ["AI-004","MUST NOT","An agent changes unrelated files merely to clean up the repository.","P0.3","review"],
  ["AI-005","MUST NOT","An agent invents values for unresolved D-01 through D-18 decisions.","P0.3:P8.1","decision validation"],
  ["AI-006","MUST","An invariant or authority conflict becomes a recorded blocker for human resolution.","P0.3","blocked event"],
  ["AI-007","MUST","Agents preserve user changes and avoid destructive Git operations.","P0.3","review"],
  ["AI-008","MUST","Interface, schema, configuration, or observable behavior changes update tests and documentation in the same pull request.","all implementation tasks","PR checklist"],
  ["AI-009","MUST NOT","Work is marked done without required evidence.","all tasks","state reducer"],
  ["AI-010","MUST","Agents record commands and checks run, results, and evidence paths.","all tasks","verification event"],
  ["AI-011","MUST NOT","An agent merges or releases its own work without required human authority.","P0.3, P8.7","ruleset"],
  ["AI-012","MUST NOT","Two agents own the same task or branch concurrently.","P0.3","event-chain conflict check"],
  ["AI-013","SHOULD","Parallel agents use separate worktrees and non-overlapping module scopes.","P0.3","coordination policy"],
  ["AI-014","MUST","A handoff records completed work, remaining work, changed files, tests, risks, blockers, and next eligible step.","P0.3","handoff schema"],
  ["AI-015","MUST","Trello and GitHub Issues are coordination mirrors; repository plan, events, evidence, and decisions remain canonical.","P0.3, P0.4","reconciliation check"],
  ["STATE-001","MUST","The immutable workbook is normalized into a versioned task catalog with stable IDs.","P0.3","catalog schema"],
  ["STATE-002","MUST","Progress changes are append-only events with unique IDs and actor, time, branch, transition, and evidence fields.","P0.3","event schema"],
  ["STATE-003","MUST","PROJECT_STATE.json is generated deterministically from the task catalog and events.","P0.3","regeneration CI"],
  ["STATE-004","MUST NOT","Agents hand-edit generated PROJECT_STATE.json after the reducer exists.","P0.3","regeneration CI"],
  ["STATE-005","MUST","Task transitions follow the documented state machine and invalid transitions fail.","P0.3","reducer tests"],
  ["STATE-006","MUST","G0 through G8 are hard gates and require explicit evidence-backed pass events.","G0:G8","gate reducer"],
  ["STATE-007","MUST NOT","Completing tasks automatically passes a gate.","G0:G8","gate reducer"],
  ["STATE-008","MUST","The current step is chosen deterministically from active, verification-pending, ready, or blocked work in sequence order.","P0.3","reducer tests"],
  ["STATE-009","MUST","Divergent event chains for one task fail reduction and require reconciliation.","P0.3","conflict test"],
  ["STATE-010","MUST","Done or passed states carry evidence that can be resolved in Git, CI, or approved lab records.","all tasks and gates","evidence validator"],
  ["STATE-011","MUST","The generated state records source artifact hashes, plan version, event count, and generation time.","P0.3","state schema"],
  ["STATE-012","MUST","If all eligible work is blocked, state mode is blocked and lists exact blocker IDs.","P0.3","reducer tests"],
  ["DOC-001","MUST","AGENTS.md is the canonical model-agnostic instruction entry point.","P0.3","documentation link check"],
  ["DOC-002","MUST","Model-specific instruction files are thin adapters and do not duplicate policy.","P0.3","documentation drift check"],
  ["DOC-003","MUST","Each module contract states purpose, boundaries, interfaces, thread ownership, schemas, config, events, failure behavior, tests, invariants, ADRs, and work-item links.","module implementation tasks","module review"],
  ["DOC-004","MUST","Architecture decisions use immutable ADR history with Proposed, Accepted, Rejected, or Superseded status.","P0.3, decisions","ADR lint"],
  ["DOC-005","MUST NOT","D-01 through D-18 are renumbered or replaced by ADR identifiers.","all decision work","ID reconciliation"],
  ["DOC-006","MUST","Documentation changes occur in the same pull request as the behavior they describe.","all tasks","PR checklist"],
  ["DATA-001","MUST NOT","Participant names, contact details, consent records, calibration, or study data enter Git or GitHub.","P0.3, P8.3","data leak scan"],
  ["DATA-002","MUST","De-identified participant IDs are treated as research data and kept out of the public repository.","P8.3","data governance review"],
  ["DATA-003","MUST","Raw study data is append-only; reruns use a unique suffix and preserve prior output.","P2.5, P4.10","G2, G4"],
  ["DATA-004","MUST","Only synthetic fixtures approved for public disclosure may be committed.","P2.9, P2.8","repository-policy CI"],
  ["DATA-005","MUST","Logs record application, commit, schema, config, scenario, session, and timing provenance.","P2.5","G2"],
  ["DATA-006","MUST","Backups include per-session checksums and follow IRB and institutional retention rules.","P8.3","G8"],
  ["DATA-007","MUST NOT","Study operation sends telemetry or research data to unapproved cloud services.","P0.3, P8.3","security review"],
  ["DATA-008","MUST","Analysis preserves raw data, performs version-aware ingest, and records migrations.","P2.8, P8.6","analysis tests"],
  ["REL-001","MUST","The first data-collection release is tagged study-v1.0 after G8 passes.","P8.7","G8"],
  ["REL-002","MUST","Study config, rig calibration, scenarios, and sessions are frozen by recorded hashes; local.json is excluded.","P8.7","frozen manifest"],
  ["REL-003","MUST","A data-collection change creates a new tag and a study-log entry.","P8.7","release policy"],
  ["REL-004","MUST","The release archive includes checksum manifest, build-info, licenses, operator docs, and diagnostics.","P8.7","release verification"],
  ["REL-005","MUST","Release creation requires human approval and cannot substitute CI for hardware gate evidence.","P8.7","release environment approval"]
].map(([id,level,requirement,implementedBy,verifiedBy]) => ({id,level,requirement,implementedBy,verifiedBy,source:id.startsWith("SCI")||id.startsWith("SAFE")?"ELTS build plan and architecture specification":"ELTS architecture specification",status:"baseline"}));

const artifacts = [
  ["ART-001","README.md","Committed","Repository entry point and quick start","P0.3"],
  ["ART-002","AGENTS.md","Committed","Canonical coding-agent instruction entry point","P0.3"],
  ["ART-003","CLAUDE.md","Committed","Thin adapter to canonical agent instructions","P0.3"],
  ["ART-004",".github/copilot-instructions.md","Committed","Thin adapter to canonical agent instructions","P0.3"],
  ["ART-005","PROJECT_STATE.json","Committed generated","Exact current progress snapshot","P0.3"],
  ["ART-006","project-management/task-catalog.json","Committed","Normalized immutable execution catalog","P0.3"],
  ["ART-007","project-management/progress/events/","Committed","Append-only progress events","P0.3"],
  ["ART-008","project-management/baseline/","Committed","Unchanged specification and workbook baseline","P0.3"],
  ["ART-009","schemas/progress/","Committed","Task, event, state, and handoff schemas","P0.3"],
  ["ART-010","config/","Committed","Sole hand-edited source of study and rig templates","P0.3, P2.1"],
  ["ART-011","unity/","Committed","Unity 6 study application source and settings","P0.3:P5.10, P6.1:P6.6"],
  ["ART-012","unity/Assets/ThirdParty/OpenVR/","Committed vendored","Pinned OpenVR binding, native library, license, and checksums","P0.3"],
  ["ART-013","jetson/","Committed","ELTS listener, protocol adapters, service, installer, and tests","P6.7:P6.10"],
  ["ART-014","analysis/","Committed","Version-aware ingest, tests, and synthetic fixtures","P2.8, P8.6"],
  ["ART-015","scripts/bootstrap-dev.ps1","Committed","Idempotent developer bootstrap","P0.3"],
  ["ART-016","scripts/doctor.ps1","Committed","Developer and study-PC diagnostics","P0.3, P4.12"],
  ["ART-017","scripts/build.ps1","Committed","Pinned command-line Unity build entry point","P0.3"],
  ["ART-018","scripts/package-study.ps1","Committed","Portable release packager","P8.7"],
  ["ART-019","scripts/setup-study-machine.ps1","Committed","FIRST-RUN implementation","P8.7"],
  ["ART-020","docs/ai/","Committed","Agent protocol, review checklist, context index, handoff template","P0.3"],
  ["ART-021","docs/adr/","Committed","Architecture decision records","P0.3, D-01:D-18"],
  ["ART-022","docs/modules/","Committed","Module contracts","all software tasks"],
  ["ART-023","docs/validation/","Committed summaries only","Bench methods, result summaries, pass or fail, secure raw-data references","P7.2:P7.7"],
  ["ART-024","docs/operator/","Committed","Daily, pre-session, run, post-session, and troubleshooting procedures","P8.2"],
  ["ART-025","data/","Never committed","Research data and participant calibration","P2.5, P8.3"],
  ["ART-026","local.json","Generated untracked","Machine-specific display and data paths","P3.5"],
  ["ART-027","ELTS-study-v*/","Release artifact","Self-contained study package with manifest and provenance","P8.7"]
].map(([id,pathName,disposition,purpose,ownerStep])=>({id,path:pathName,disposition,purpose,ownerStep,notes:"Path is relative to repository or release root."}));

const progressFields = [
  ["$schema","string","yes","Relative path to schemas/progress/progress-state.schema.json","schemas/progress/progress-state.schema.json"],
  ["schemaVersion","integer","yes","Progress-state schema version","1"],
  ["project","string","yes","Stable project name","DESS-ELTS-Research-Software"],
  ["planVersion","string","yes","Workbook/build-plan baseline","ELTS-build-plan-1.0"],
  ["generatedAtUtc","date-time","yes","UTC time produced by reducer","<YYYY-MM-DDTHH:MM:SSZ>"],
  ["generatedFrom.workbookSha256","sha256","yes","Hash of preserved baseline workbook","<64 hex>"],
  ["generatedFrom.specificationSha256","sha256","yes","Hash of preserved specification","<64 hex>"],
  ["generatedFrom.taskCatalogSha256","sha256","yes","Hash of normalized catalog","<64 hex>"],
  ["generatedFrom.eventCount","integer","yes","Number of accepted progress events","1"],
  ["current.mode","enum","yes","working, blocked, verification, complete","working"],
  ["current.phaseId","ID","yes","Earliest phase whose gate has not passed","P0"],
  ["current.gateId","ID","yes","Gate associated with current phase","G0"],
  ["current.primaryStepId","ID","yes","Deterministic exact current atomic step","P0.3.S001"],
  ["current.activeStepIds","ID array","yes","All in-progress atomic steps in current phase","[P0.3.S001]"],
  ["current.nextEligibleStepIds","ID array","yes","Ready steps after dependency evaluation","[]"],
  ["current.blockingIds","ID array","yes","Exact task, decision, gate, or external blockers","[]"],
  ["activeWork[].owner","string","yes","Human or agent identity class and stable identifier","agent:codex"],
  ["activeWork[].branch","string","yes","Single task branch","bootstrap/repository-governance"],
  ["tasks.<id>.status","enum","yes","Status derived from events","in_progress"],
  ["tasks.<id>.evidence","array","yes","Resolvable evidence objects","[]"],
  ["decisions.<id>.status","enum","yes","Open, in review, decided, superseded, informational","open"],
  ["gates.<id>.status","enum","yes","Locked, open, verification pending, passed, failed","open"],
  ["integrity.reducerVersion","string","yes","Version or commit of deterministic reducer","1.0.0"]
].map(([field,type,required,rule,example])=>({field,type,required,rule,example}));

function styleTitle(sheet, title, subtitle, endColumn="H") {
  sheet.showGridLines = false;
  sheet.getRange(`A1:${endColumn}1`).merge();
  sheet.getRange("A1").values = [[title]];
  sheet.getRange("A1").format.font = {name:CONFIG.font,size:18,bold:true,color:CONFIG.colors.black};
  sheet.getRange("A1").format.rowHeight = 28;
  sheet.getRange(`A2:${endColumn}2`).merge();
  sheet.getRange("A2").values = [[subtitle]];
  sheet.getRange("A2").format.font = {name:CONFIG.font,size:10,italic:true,color:CONFIG.colors.gray};
  sheet.getRange(`A3:${endColumn}3`).format.borders = {bottom:{style:"thin",color:CONFIG.colors.navy}};
}

function styleHeader(range) {
  range.format.fill = CONFIG.colors.navy;
  range.format.font = {name:CONFIG.font,size:10,bold:true,color:CONFIG.colors.white};
  range.format.horizontalAlignment = "center";
  range.format.verticalAlignment = "center";
  range.format.wrapText = true;
  range.format.borders = {preset:"all",style:"thin",color:CONFIG.colors.white};
  range.format.rowHeight = 32;
}

function styleBody(range) {
  range.format.font = {name:CONFIG.font,size:10,color:CONFIG.colors.black};
  range.format.verticalAlignment = "top";
  range.format.wrapText = true;
  range.format.borders = {bottom:{style:"thin",color:CONFIG.colors.border}};
}

function addStatusFormatting(range) {
  range.conditionalFormats.add("containsText", {text:"done", format:{fill:CONFIG.colors.green,font:{color:"#375623",bold:true}}});
  range.conditionalFormats.add("containsText", {text:"passed", format:{fill:CONFIG.colors.green,font:{color:"#375623",bold:true}}});
  range.conditionalFormats.add("containsText", {text:"in_progress", format:{fill:CONFIG.colors.blue,font:{color:CONFIG.colors.navy,bold:true}}});
  range.conditionalFormats.add("containsText", {text:"verification_pending", format:{fill:CONFIG.colors.purple,font:{color:"#403151",bold:true}}});
  range.conditionalFormats.add("containsText", {text:"ready", format:{fill:CONFIG.colors.amber,font:{color:"#7F6000",bold:true}}});
  range.conditionalFormats.add("containsText", {text:"blocked", format:{fill:CONFIG.colors.red,font:{color:"#9C0006",bold:true}}});
  range.conditionalFormats.add("containsText", {text:"fail", format:{fill:CONFIG.colors.red,font:{color:"#9C0006",bold:true}}});
}

function writeTableSheet(sheet, title, subtitle, headers, rows, widths, options={}) {
  const lastCol = colLetter(headers.length);
  styleTitle(sheet,title,subtitle,lastCol);
  const startRow = 5;
  sheet.getRange(`A${startRow}:${lastCol}${startRow}`).values = [headers];
  styleHeader(sheet.getRange(`A${startRow}:${lastCol}${startRow}`));
  if (rows.length) {
    sheet.getRange(`A${startRow+1}`).write(rows);
    styleBody(sheet.getRange(`A${startRow+1}:${lastCol}${startRow+rows.length}`));
    for (let r = startRow+1; r <= startRow+rows.length; r++) {
      if ((r-startRow)%2===0) sheet.getRange(`A${r}:${lastCol}${r}`).format.fill = CONFIG.colors.paleBlue;
    }
  }
  widths.forEach((w,i)=>{ sheet.getRange(`${colLetter(i+1)}:${colLetter(i+1)}`).format.columnWidth = w; });
  sheet.freezePanes.freezeRows(startRow);
  if (options.freezeCols) sheet.freezePanes.freezeColumns(options.freezeCols);
  if (options.tableName && rows.length) {
    const table = sheet.tables.add(`A${startRow}:${lastCol}${startRow+rows.length}`, true, options.tableName);
    table.showFilterButton = true;
    table.showBandedRows = false;
  }
  return {startRow,endRow:startRow+rows.length,lastCol};
}

function colLetter(n) {
  let s="";
  while (n>0) { n--; s=String.fromCharCode(65+n%26)+s; n=Math.floor(n/26); }
  return s;
}

async function main() {
  const markdown = await fs.readFile(CONFIG.sourcePlan,"utf8");
  const tasks = parseTasks(markdown);
  const decisions = parseDecisions(markdown);
  const gates = parseGates(markdown);
  const risks = parseRisks(markdown);
  const atomic = atomicStepsFor(tasks);
  if (tasks.length !== 91) throw new Error(`Expected 91 P tasks, found ${tasks.length}`);
  if (decisions.length !== 18) throw new Error(`Expected 18 decisions, found ${decisions.length}`);
  if (gates.length !== 62) throw new Error(`Expected 62 gate criteria, found ${gates.length}`);

  const wb = Workbook.create();
  const start = wb.worksheets.add("Start Here");
  const dash = wb.worksheets.add("Dashboard");
  const phases = wb.worksheets.add("Phases");
  const wi = wb.worksheets.add("Work Items");
  const at = wb.worksheets.add("Atomic Steps");
  const ds = wb.worksheets.add("Decisions");
  const gs = wb.worksheets.add("Gate Criteria");
  const dep = wb.worksheets.add("Dependencies");
  const req = wb.worksheets.add("Requirements Traceability");
  const art = wb.worksheets.add("Artifacts");
  const riskSheet = wb.worksheets.add("Risks");
  const evidenceSheet = wb.worksheets.add("Evidence");
  const pc = wb.worksheets.add("Progress Contract");
  const sessions = wb.worksheets.add("Agent Sessions");
  const changes = wb.worksheets.add("Plan Changes");
  const lists = wb.worksheets.add("Reference Lists");

  // Start Here
  styleTitle(start,"ELTS implementation workbook","Execution baseline for repository architecture, portability, scientific integrity, and multi-agent work","J");
  const startRows = [
    ["Workbook role","Immutable bootstrap plan. After import, project-management/task-catalog.json is the machine-readable plan and PROJECT_STATE.json is the live generated status."],
    ["Specification role",`${CONFIG.specificationFile} is the normative architecture contract. Scientific and safety invariants outrank all lower-level artifacts.`],
    ["Initial exact step","P0.3.S001 - inventory the repository without modifying or discarding existing work."],
    ["First write","Create the progress schemas, initial append-only event, reducer entry point, and generated PROJECT_STATE.json before making other architecture changes."],
    ["Source of truth order","Approved research protocol and safety constraints; architecture specification; accepted ADRs; module contracts; this baseline plan and normalized task catalog; code comments."],
    ["Live status precedence","PROJECT_STATE.json and its append-only source events override the editable Status columns in this workbook. Trello and GitHub Issues are coordination mirrors."],
    ["Agent rule","Read both deliverables in full, preserve unchanged copies, export normalized data, claim one bounded task, record a start event, implement, verify, attach evidence, and record a completion or blocker event."],
    ["Decision rule","Never invent D-01 through D-18 values. Record a blocker or prepare an ADR when a human decision is required."],
    ["Gate rule","G0 through G8 are hard gates. Completed tasks do not pass a gate. A gate requires an explicit evidence-backed pass event and any required human attestation."],
    ["Repository",CONFIG.repositoryUrl],
    ["Baseline date",CONFIG.baselineDate],
    ["Build-plan SHA-256",CONFIG.sourcePlanSha256]
  ];
  start.getRange("A5:B16").values = startRows;
  start.getRange("A5:A16").format = {fill:CONFIG.colors.paleGray,font:{name:CONFIG.font,size:10,bold:true,color:CONFIG.colors.black},verticalAlignment:"top",wrapText:true};
  start.getRange("B5:B16").format = {font:{name:CONFIG.font,size:10,color:CONFIG.colors.black},verticalAlignment:"top",wrapText:true};
  start.getRange("A5:B16").format.borders = {preset:"all",style:"thin",color:CONFIG.colors.border};
  start.getRange("A:A").format.columnWidth = 25;
  start.getRange("B:B").format.columnWidth = 105;
  start.getRange("A18:J18").merge(); start.getRange("A18").values=[["Initial coding-agent directive"]];
  start.getRange("A18").format={fill:CONFIG.colors.navy,font:{name:CONFIG.font,size:11,bold:true,color:CONFIG.colors.white}};
  const directive = "Read the specification and this workbook in full before editing. Inventory the repository and preserve all existing work. Create the event-sourced progress system first and make PROJECT_STATE.json report P0.3.S001 or the next valid atomic step. Preserve both source artifacts under project-management/baseline, hash them, export every registry to versioned JSON or CSV, and validate stable IDs. Work on a temporary branch. Do not push directly to main, invent open research decisions, weaken safety controls, place research data in Git, or mark work done without evidence. Finish each task by running its validation, updating affected documentation, emitting a progress event, regenerating PROJECT_STATE.json, and leaving a complete handoff.";
  start.getRange("A19:J23").merge(); start.getRange("A19").values=[[directive]];
  start.getRange("A19").format={font:{name:CONFIG.font,size:11,color:CONFIG.colors.black},wrapText:true,verticalAlignment:"top"};
  start.getRange("A19:J23").format.borders={preset:"outside",style:"thin",color:CONFIG.colors.border};
  start.freezePanes.freezeRows(3);

  // Work items
  const workHeaders=["Sequence","Work Item ID","Phase","Phase Name","Gate","Workstream","Title","Action and substeps","Entry Criteria","Direct Dependencies","Blocking Decisions","Completion Evidence","Validation","Expected Artifacts","Owner Role","Execution Class","Parallel Lane","Initial Status","Live Status","Branch or PR","Evidence Path or URL","Notes","Source"];
  const workRows=tasks.map((t,i)=>[i+1,t.id,t.phase,t.phaseName,t.gate,t.workstream,t.title,t.action,t.entry,t.dependencies,t.decisions,t.done,t.validation,t.artifacts,t.owner,t.executionClass,t.lane,"not_started","not_started","","","",t.source]);
  const workInfo=writeTableSheet(wi,"ELTS work items","All 91 P0-P8 work items from the build plan. Status is a planning aid; generated repository state is authoritative.",workHeaders,workRows,[9,13,8,22,8,14,30,65,36,28,20,44,42,38,24,16,25,15,15,28,30,30,22],{freezeCols:2,tableName:"WorkItemsTable"});
  wi.getRange(`R6:R${workInfo.endRow}`).dataValidation={rule:{type:"list",values:STATUS}};
  wi.getRange(`P6:P${workInfo.endRow}`).dataValidation={rule:{type:"list",values:EXECUTION_CLASS}};
  addStatusFormatting(wi.getRange(`S6:S${workInfo.endRow}`));

  // Atomic steps
  const atomicHeaders=["Sequence","Atomic Step ID","Parent Work Item","Phase","Gate","Workstream","Step Type","Action","Depends On","Blocking Decisions","Required Evidence","Validation","Expected Artifacts","Status","Owner","Execution Class","Suggested Branch","Started UTC","Completed UTC","Evidence Path or URL","Notes","Source"];
  const atomicRows=atomic.map(s=>[s.seq,s.id,s.parent,s.phase,s.gate,s.workstream,s.stepType,s.action,s.dependencies,s.decisions,s.evidence,s.validation,s.artifacts,s.status,s.owner,s.executionClass,s.branch,"","","","",s.source]);
  const atomicInfo=writeTableSheet(at,"Atomic ELTS steps",`${atomic.length} ordered actions. PROJECT_STATE.json identifies the exact current atomic step using this sequence.`,atomicHeaders,atomicRows,[9,18,15,8,8,14,16,68,26,20,43,43,34,20,24,16,38,19,19,30,30,22],{freezeCols:3,tableName:"AtomicStepsTable"});
  at.getRange(`N6:N${atomicInfo.endRow}`).dataValidation={rule:{type:"list",values:STATUS}};
  at.getRange(`P6:P${atomicInfo.endRow}`).dataValidation={rule:{type:"list",values:EXECUTION_CLASS}};
  at.getRange(`R6:S${atomicInfo.endRow}`).setNumberFormat("yyyy-mm-dd hh:mm");
  addStatusFormatting(at.getRange(`N6:N${atomicInfo.endRow}`));

  // Parent work-item status is derived from child atomic-step status rather than hand-entered.
  for (let r=6; r<=workInfo.endRow; r++) {
    wi.getRange(`S${r}`).formulas=[[`=IF(COUNTIFS('Atomic Steps'!$C$6:$C$${atomicInfo.endRow},B${r},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"blocked\")>0,\"blocked\",IF(COUNTIFS('Atomic Steps'!$C$6:$C$${atomicInfo.endRow},B${r},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"in_progress\")>0,\"in_progress\",IF(COUNTIFS('Atomic Steps'!$C$6:$C$${atomicInfo.endRow},B${r},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"verification_pending\")>0,\"verification_pending\",IF(COUNTIFS('Atomic Steps'!$C$6:$C$${atomicInfo.endRow},B${r},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"done\")=COUNTIF('Atomic Steps'!$C$6:$C$${atomicInfo.endRow},B${r}),\"done\",IF(COUNTIFS('Atomic Steps'!$C$6:$C$${atomicInfo.endRow},B${r},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"ready\")>0,\"ready\",\"not_started\")))))`]];
  }

  // Phase and gate roll-up.
  const phaseHeaders=["Phase ID","Order","Title","Goal","Entry Criteria","Gate","Depends On","Minimum Days","Maximum Days","Critical Path","Atomic Steps","Done Steps","Completion","Gate Status","Phase Status"];
  const phaseGoals={
    P0:"Put procurement, study PC, repository, decisions, safety documentation, and room information in place.",
    P1:"Deliver reliable headless tracking, prove uncertain API behavior, and begin physical fabrication.",
    P2:"Build the tested nonvisual backbone: configuration, clock, tracking, geometry, logging, events, ingest, and synthetic tracking.",
    P3:"Deliver correct participant and operator render paths, display management, performance evidence, and replay.",
    P4:"Run a complete operator-driven dry session using data-driven targets, scoring, feedback, blocks, and self-checks.",
    P5:"Integrate the weapon and complete rig and participant calibration workflows with recorded residuals.",
    P6:"Integrate Unity and Jetson lifecycle control with timestamps, heartbeat, acknowledgements, and independent fail-safes.",
    P7:"Measure the apparatus risks, complete safety evidence, and freeze rig-derived thresholds.",
    P8:"Prove operator independence, cross-device alignment, analysis, backup, release packaging, and readiness for Participant 1."
  };
  const estimates={P0:[3,5],P1:[5,8],P2:[8,12],P3:[6,10],P4:[10,15],P5:[8,12],P6:[8,13],P7:[5,8],P8:[5,10]};
  const phaseRows=[];
  for(let p=0;p<=8;p++){
    const pid=`P${p}`, meta=phaseMeta(pid), rowNumber=6+p, est=estimates[pid];
    phaseRows.push([pid,p,meta.name,phaseGoals[pid],meta.entry,meta.gate,meta.dependency,est[0],est[1],p===1||p===2||p===3||p===4||p===5||p===7||p===8?"yes":"no",`=COUNTIF('Atomic Steps'!$D$6:$D$${atomicInfo.endRow},A${rowNumber})`,`=COUNTIFS('Atomic Steps'!$D$6:$D$${atomicInfo.endRow},A${rowNumber},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"done\")`,`=IF(K${rowNumber}=0,0,L${rowNumber}/K${rowNumber})`,"",""
    ]);
  }
  const phaseInfo=writeTableSheet(phases,"ELTS phases","Phase progress derives from atomic steps; gate state derives from evidence-backed criterion status.",phaseHeaders,phaseRows,[11,8,24,65,52,9,30,14,14,14,14,14,14,18,18],{freezeCols:2,tableName:"PhasesTable"});
  phases.getRange(`M6:M${phaseInfo.endRow}`).setNumberFormat("0.0%");
  phases.getRange(`J6:J${phaseInfo.endRow}`).dataValidation={rule:{type:"list",values:["yes","no"]}};

  // Decisions
  const decisionHeaders=["Decision ID","Decision","Owner","Latest Blocking Point","Inputs and Criteria","Status","Decision Date UTC","Outcome","ADR or Evidence","Affected Work Items","Notes"];
  const decisionRows=decisions.map(d=>[d.id,d.decision,d.owner,d.blocks,d.criteria,d.status,"","","",tasks.filter(t=>t.decisions.split(", ").includes(d.id)).map(t=>t.id).join(", "),""]);
  const decisionInfo=writeTableSheet(ds,"Decision register","D-01 through D-18 retain their original identifiers. Agents must not invent outcomes.",decisionHeaders,decisionRows,[13,36,27,22,72,16,20,44,30,30,28],{freezeCols:2,tableName:"DecisionsTable"});
  ds.getRange(`F6:F${decisionInfo.endRow}`).dataValidation={rule:{type:"list",values:DECISION_STATUS}};
  addStatusFormatting(ds.getRange(`F6:F${decisionInfo.endRow}`));

  // Gate criteria
  const gateHeaders=["Gate","Criterion ID","Criterion","Source","Required Evidence","Verifier","Status","Verified UTC","Evidence Path or URL","Notes"];
  const gateRows=gates.map(g=>[g.gate,g.criterionId,g.criterion,g.source,"Test output, document, measurement, or signed lab record that directly proves the criterion.",g.gate==="G8"||g.gate==="G6"||g.gate==="G7"?"Human approver and developer":"Developer or assigned owner",g.status,"","",""]);
  const gateInfo=writeTableSheet(gs,"Gate criteria","All 62 hard-gate criteria. A gate passes only through an explicit pass event after every applicable criterion has evidence.",gateHeaders,gateRows,[9,13,76,27,52,30,18,20,32,30],{freezeCols:2,tableName:"GateCriteriaTable"});
  gs.getRange(`G6:G${gateInfo.endRow}`).dataValidation={rule:{type:"list",values:CRITERION_STATUS}};
  gs.getRange(`H6:H${gateInfo.endRow}`).setNumberFormat("yyyy-mm-dd hh:mm");
  addStatusFormatting(gs.getRange(`G6:G${gateInfo.endRow}`));

  // Complete the phase formulas now that gate ranges are known.
  for(let r=6;r<=phaseInfo.endRow;r++){
    phases.getRange(`N${r}`).formulas=[[`=IF(COUNTIFS('Gate Criteria'!$A$6:$A$${gateInfo.endRow},F${r},'Gate Criteria'!$G$6:$G$${gateInfo.endRow},\"fail\")>0,\"failed\",IF(COUNTIFS('Gate Criteria'!$A$6:$A$${gateInfo.endRow},F${r},'Gate Criteria'!$G$6:$G$${gateInfo.endRow},\"blocked\")>0,\"blocked\",IF(COUNTIFS('Gate Criteria'!$A$6:$A$${gateInfo.endRow},F${r},'Gate Criteria'!$G$6:$G$${gateInfo.endRow},\"pass\")+COUNTIFS('Gate Criteria'!$A$6:$A$${gateInfo.endRow},F${r},'Gate Criteria'!$G$6:$G$${gateInfo.endRow},\"not_applicable\")=COUNTIF('Gate Criteria'!$A$6:$A$${gateInfo.endRow},F${r}),\"passed\",IF(COUNTIFS('Gate Criteria'!$A$6:$A$${gateInfo.endRow},F${r},'Gate Criteria'!$G$6:$G$${gateInfo.endRow},\"pass\")>0,\"in_progress\",\"not_evaluated\"))))`]];
    phases.getRange(`O${r}`).formulas=[[`=IF(N${r}=\"passed\",\"complete\",IF(OR(N${r}=\"failed\",N${r}=\"blocked\"),\"blocked\",IF(COUNTIFS('Atomic Steps'!$D$6:$D$${atomicInfo.endRow},A${r},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"in_progress\")+COUNTIFS('Atomic Steps'!$D$6:$D$${atomicInfo.endRow},A${r},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"verification_pending\")>0,\"in_progress\",\"not_started\")))`]];
  }
  addStatusFormatting(phases.getRange(`N6:O${phaseInfo.endRow}`));

  // Dependencies
  function expandTokens(text){
    if(!text) return [];
    const cleaned=text.replace(/[()]/g,"");
    const pieces=cleaned.split(/,|\band\b|\bor\b/).map(x=>x.trim()).filter(Boolean);
    const out=[];
    for(const piece of pieces){
      const range=piece.match(/^(P\d+)\.(\d+):(P\d+\.)?(\d+)$/);
      if(range){const prefix=range[1]; for(let n=Number(range[2]);n<=Number(range[4]);n++) out.push(`${prefix}.${n}`);}
      else if(/^(P\d+\.\d+|G\d+|D-\d{2})$/.test(piece)) out.push(piece);
      else if(piece.includes("P2.9")) out.push("P2.9");
    }
    return [...new Set(out)];
  }
  const dependencyEdges=[]; let depSeq=1;
  for(const t of tasks){
    const sources=[...expandTokens(phaseMeta(t.phase).dependency),...expandTokens(t.dependencies),...expandTokens(t.decisions)];
    for(const predecessor of [...new Set(sources)]) dependencyEdges.push([`DEP-${String(depSeq++).padStart(4,"0")}`,predecessor,t.id,predecessor.startsWith("D-")?"blocks":predecessor.startsWith("G")?"unlocks":"requires","yes",predecessor.startsWith("D-")?"decided":predecessor.startsWith("G")?"passed":"done",t.phase==="P2"&&predecessor==="G1"?"May use P2.9 synthetic tracking after G0 until real-pose evidence is required.":"All listed conditions apply unless an accepted ADR states otherwise.",t.source,""]);
  }
  const depHeaders=["Dependency ID","Predecessor ID","Successor ID","Relation","Hard Dependency","Required State","Condition","Source","Notes"];
  writeTableSheet(dep,"Dependency register","Normalized edge list for eligibility calculation. Conditional exceptions are stated explicitly.",depHeaders,dependencyEdges,[15,17,17,15,17,17,68,25,30],{freezeCols:3,tableName:"DependenciesTable"});

  // Requirements
  const reqHeaders=["Requirement ID","Level","Requirement","Implemented By","Verified By","Source","Status","Evidence","Notes"];
  const reqRows=requirements.map(r=>[r.id,r.level,r.requirement,r.implementedBy,r.verifiedBy,r.source,r.status,"",""]);
  const reqInfo=writeTableSheet(req,"Requirements traceability","Normative architecture, scientific, portability, data, Git, agent, progress, and release requirements.",reqHeaders,reqRows,[15,13,80,28,28,30,16,34,30],{freezeCols:2,tableName:"RequirementsTable"});
  req.getRange(`B6:B${reqInfo.endRow}`).dataValidation={rule:{type:"list",values:REQUIREMENT_LEVEL}};

  // Artifacts
  const artHeaders=["Artifact ID","Path or Package","Disposition","Purpose","Owning Step or Range","Notes","Status","Evidence"];
  const artRows=artifacts.map(a=>[a.id,a.path,a.disposition,a.purpose,a.ownerStep,a.notes,"not_started",""]);
  const artInfo=writeTableSheet(art,"Artifact register","Expected repository, generated, untracked, and release artifacts with a single declared role.",artHeaders,artRows,[13,42,22,55,25,42,18,34],{freezeCols:2,tableName:"ArtifactsTable"});
  art.getRange(`G6:G${artInfo.endRow}`).dataValidation={rule:{type:"list",values:STATUS}};
  addStatusFormatting(art.getRange(`G6:G${artInfo.endRow}`));

  // Risks
  const riskHeaders=["Risk ID","Risk","Effect","Mitigation","Verified By","Owner","Status","Evidence or Review Date","Notes"];
  const riskRows=risks.map(r=>[r.id,r.risk,r.effect,r.mitigation,r.verifiedBy,r.owner,r.status,"",""]);
  const riskInfo=writeTableSheet(riskSheet,"Risk register","Build-plan risks plus repository portability, data, and multi-agent governance risks.",riskHeaders,riskRows,[12,48,42,66,25,28,15,28,30],{freezeCols:1,tableName:"RisksTable"});
  riskSheet.getRange(`G6:G${riskInfo.endRow}`).dataValidation={rule:{type:"list",values:["open","monitoring","mitigated","accepted","closed"]}};

  // Evidence registry template.
  const evidenceHeaders=["Evidence ID","Target ID","Evidence Type","Repository Relative Path or URL","Verification Command or Method","Result","SHA-256","Recorded By","Recorded UTC","Approval or Signoff","Notes"];
  const evidenceRows=Array.from({length:30},()=>Array(evidenceHeaders.length).fill(""));
  const evidenceInfo=writeTableSheet(evidenceSheet,"Evidence register","Template for resolvable evidence. Do not enter absolute local paths, participant identifiers, secrets, or raw study data.",evidenceHeaders,evidenceRows,[17,17,20,52,58,16,42,22,20,28,34],{freezeCols:2,tableName:"EvidenceTable"});
  evidenceSheet.getRange(`C6:C${evidenceInfo.endRow}`).dataValidation={rule:{type:"list",values:["commit","test_report","command_output","file","photo","measurement","signoff","issue","release","checksum"]}};
  evidenceSheet.getRange(`F6:F${evidenceInfo.endRow}`).dataValidation={rule:{type:"list",values:["pass","fail","informational"]}};
  evidenceSheet.getRange(`I6:I${evidenceInfo.endRow}`).setNumberFormat("yyyy-mm-dd hh:mm");

  // Progress contract
  const pcHeaders=["Field Path","Type","Required","Rule","Example"];
  const pcRows=progressFields.map(f=>[f.field,f.type,f.required,f.rule,f.example]);
  const pcInfo=writeTableSheet(pc,"Progress-state contract","PROJECT_STATE.json is generated from the task catalog and append-only events; agents do not hand-edit it after bootstrap.",pcHeaders,pcRows,[40,18,12,78,42],{freezeCols:1,tableName:"ProgressFieldsTable"});
  let row=pcInfo.endRow+3;
  pc.getRange(`A${row}:E${row}`).merge(); pc.getRange(`A${row}`).values=[["Allowed task transitions"]];
  pc.getRange(`A${row}`).format={fill:CONFIG.colors.navy,font:{name:CONFIG.font,size:10,bold:true,color:CONFIG.colors.white}};
  const transitions=[
    ["not_started","ready","Dependencies and phase gate permit work"],
    ["ready","in_progress","One owner claims the task on one branch"],
    ["in_progress","verification_pending","Implementation is complete and evidence checks are running"],
    ["verification_pending","done","Every acceptance criterion has resolvable evidence"],
    ["verification_pending","in_progress","Verification failed and remediation resumes"],
    ["ready or in_progress or verification_pending","blocked","Exact blocker and next required action are recorded"],
    ["blocked","ready or in_progress","Blocker is resolved by an event"],
    ["done","in_progress","Explicit reopen event with reason and human authorization where required"]
  ];
  pc.getRange(`A${row+1}:C${row+1}`).values=[["From","To","Condition"]]; styleHeader(pc.getRange(`A${row+1}:C${row+1}`));
  pc.getRange(`A${row+2}`).write(transitions); styleBody(pc.getRange(`A${row+2}:C${row+1+transitions.length}`));
  row += transitions.length+4;
  pc.getRange(`A${row}:E${row}`).merge(); pc.getRange(`A${row}`).values=[["Deterministic exact-current-step algorithm"]]; pc.getRange(`A${row}`).format={fill:CONFIG.colors.navy,font:{name:CONFIG.font,size:10,bold:true,color:CONFIG.colors.white}};
  const algorithm=[
    [1,"Choose the earliest phase whose gate has not passed."],
    [2,"Within that phase, collect all valid in_progress atomic steps."],
    [3,"Set primaryStepId to the lowest workbook sequence among active steps."],
    [4,"If none are active, choose the lowest verification_pending step."],
    [5,"If none await verification, choose the lowest ready step."],
    [6,"If all eligible work is blocked, set mode to blocked and list the exact blocker IDs."],
    [7,"After an explicit evidence-backed G8 pass event, set mode to complete."],
    [8,"If two events claim the same previous task event, fail reduction and require reconciliation."]
  ];
  pc.getRange(`A${row+1}:B${row+1}`).values=[["Order","Rule"]]; styleHeader(pc.getRange(`A${row+1}:B${row+1}`));
  pc.getRange(`A${row+2}`).write(algorithm); styleBody(pc.getRange(`A${row+2}:B${row+1+algorithm.length}`));
  pc.getRange(`A${row+2}:A${row+1+algorithm.length}`).format.horizontalAlignment="center";

  // Agent sessions
  const sessionHeaders=["Session ID","Actor","Model or Tool","Task ID","Atomic Step ID","Branch","Started UTC","Ended UTC","Outcome","Files Changed","Checks Run","Evidence","Blockers","Next Step","Handoff Path"];
  const sessionRows=Array.from({length:20},()=>Array(sessionHeaders.length).fill(""));
  const sessionInfo=writeTableSheet(sessions,"Agent session handoff log","Template for durable handoffs. The repository event store remains authoritative.",sessionHeaders,sessionRows,[20,20,20,13,18,34,20,20,18,45,45,34,34,18,34],{freezeCols:2,tableName:"AgentSessionsTable"});
  sessions.getRange(`G6:H${sessionInfo.endRow}`).setNumberFormat("yyyy-mm-dd hh:mm");

  // Append-only plan change template.
  const changeHeaders=["Change ID","Plan Revision","Requested UTC","Requested By","Affected IDs","Reason","Proposed Change","Architecture or Protocol Impact","Approval Required From","Decision","Approved By","Approved UTC","Applied Commit","Notes"];
  const changeRows=Array.from({length:20},()=>Array(changeHeaders.length).fill(""));
  const changeInfo=writeTableSheet(changes,"Plan changes","Append-only revision log. Never renumber an existing P, D, G, requirement, or atomic-step ID.",changeHeaders,changeRows,[16,14,20,22,28,48,52,52,30,18,24,20,42,34],{freezeCols:2,tableName:"PlanChangesTable"});
  changes.getRange(`C6:C${changeInfo.endRow}`).setNumberFormat("yyyy-mm-dd hh:mm");
  changes.getRange(`L6:L${changeInfo.endRow}`).setNumberFormat("yyyy-mm-dd hh:mm");

  // Reference lists
  styleTitle(lists,"Reference lists","Controlled vocabulary used by workbook validation and repository schemas.","F");
  const listColumns=[
    ["Task Status",...STATUS],
    ["Decision Status",...DECISION_STATUS],
    ["Gate Status",...GATE_STATUS],
    ["Criterion Status",...CRITERION_STATUS],
    ["Execution Class",...EXECUTION_CLASS],
    ["Requirement Level",...REQUIREMENT_LEVEL]
  ];
  const maxLen=Math.max(...listColumns.map(c=>c.length));
  const matrix=Array.from({length:maxLen},(_,r)=>listColumns.map(c=>c[r]??""));
  lists.getRange("A5").write(matrix);
  styleHeader(lists.getRange("A5:F5"));
  styleBody(lists.getRange(`A6:F${4+maxLen}`));
  for(let i=0;i<6;i++) lists.getRange(`${colLetter(i+1)}:${colLetter(i+1)}`).format.columnWidth=24;

  // Dashboard is added after data ranges are known.
  styleTitle(dash,"ELTS progress dashboard","Formula-driven view of the workbook baseline. The repository-generated PROJECT_STATE.json is authoritative after bootstrap.","L");
  dash.getRange("A5:B13").values=[
    ["Metric","Value"],
    ["Atomic steps",`=ROWS('Atomic Steps'!$B$6:$B$${atomicInfo.endRow})`],
    ["Done",`=COUNTIF('Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"done\")`],
    ["In progress",`=COUNTIF('Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"in_progress\")`],
    ["Blocked",`=COUNTIF('Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"blocked\")`],
    ["Verification pending",`=COUNTIF('Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"verification_pending\")`],
    ["Open decisions",`=COUNTIF(Decisions!$F$6:$F$${decisionInfo.endRow},\"open\")`],
    ["Passed gate criteria",`=COUNTIF('Gate Criteria'!$G$6:$G$${gateInfo.endRow},\"pass\")`],
    ["Overall completion",`=IF(B6=0,0,B7/B6)`]
  ];
  styleHeader(dash.getRange("A5:B5")); styleBody(dash.getRange("A6:B13"));
  dash.getRange("B13").setNumberFormat("0.0%");
  dash.getRange("A15:B20").values=[
    ["Current-state field","Baseline value"],
    ["Initial exact step","P0.3.S001"],
    ["Initial phase","P0"],
    ["Current gate","G0 open"],
    ["Canonical live file","PROJECT_STATE.json"],
    ["Update method","Append progress event, run reducer, commit regenerated state"]
  ];
  styleHeader(dash.getRange("A15:B15")); styleBody(dash.getRange("A16:B20"));
  dash.getRange("A22:E22").values=[["Phase","Total Steps","Done","Completion","Gate"]]; styleHeader(dash.getRange("A22:E22"));
  const dashPhaseRows=[];
  for(let p=0;p<=8;p++) {
    const rr=23+p;
    dashPhaseRows.push([`P${p}`,`=COUNTIF('Atomic Steps'!$D$6:$D$${atomicInfo.endRow},A${rr})`,`=COUNTIFS('Atomic Steps'!$D$6:$D$${atomicInfo.endRow},A${rr},'Atomic Steps'!$N$6:$N$${atomicInfo.endRow},\"done\")`,`=IF(B${rr}=0,0,C${rr}/B${rr})`,`G${p}`]);
  }
  dash.getRange("A23").write(dashPhaseRows); styleBody(dash.getRange("A23:E31")); dash.getRange("D23:D31").setNumberFormat("0.0%");
  dash.getRange("A:A").format.columnWidth=26; dash.getRange("B:B").format.columnWidth=28; dash.getRange("C:E").format.columnWidth=16;
  dash.showGridLines=false;
  const chart=dash.charts.add("bar",[dash.getRange("A22:A31"),dash.getRange("D22:D31")]);
  chart.title="Completion by phase"; chart.titleTextStyle.typeface=CONFIG.font; chart.titleTextStyle.fontSize=12;
  chart.hasLegend=false; chart.xAxis={axisType:"textAxis",textStyle:{typeface:CONFIG.font,fontSize:10}};
  chart.yAxis={numberFormatCode:"0%",numberFormatSourceLinked:false,textStyle:{typeface:CONFIG.font,fontSize:10}};
  chart.setPosition("G5","N22");
  dash.freezePanes.freezeRows(3);

  // Consistent tab colors.
  start.tabColor=CONFIG.colors.navy; dash.tabColor="#5B9BD5"; phases.tabColor="#5B9BD5"; wi.tabColor="#70AD47"; at.tabColor="#70AD47";
  ds.tabColor="#FFC000"; gs.tabColor="#C00000"; dep.tabColor="#A5A5A5"; req.tabColor="#4472C4";
  art.tabColor="#4472C4"; riskSheet.tabColor="#C00000"; evidenceSheet.tabColor="#4472C4"; pc.tabColor="#7030A0"; sessions.tabColor="#7030A0"; changes.tabColor="#7030A0"; lists.tabColor="#A5A5A5";

  await fs.mkdir(CONFIG.outputDir,{recursive:true});
  const output=await SpreadsheetFile.exportXlsx(wb);
  const outputPath=path.join(CONFIG.outputDir,CONFIG.outputFile);
  await output.save(outputPath);

  // Compact inspections required by the spreadsheet workflow.
  const checks={
    inventory:await wb.inspect({kind:"sheet",include:"id,name",maxChars:5000}),
    dashboard:await wb.inspect({kind:"table",sheetId:"Dashboard",range:"A1:N31",include:"values,formulas",tableMaxRows:40,tableMaxCols:14,maxChars:12000}),
    atomic:await wb.inspect({kind:"table",sheetId:"Atomic Steps",range:`A1:N20`,include:"values,formulas",tableMaxRows:20,tableMaxCols:14,maxChars:12000}),
    errors:await wb.inspect({kind:"match",searchTerm:"#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!",options:{useRegex:true,maxResults:300},summary:"final formula error scan",maxChars:12000})
  };
  await fs.writeFile(path.join(CONFIG.outputDir,"workbook_inspection.json"),JSON.stringify({
    counts:{tasks:tasks.length,atomic:atomic.length,decisions:decisions.length,gates:gates.length,requirements:requirements.length,risks:risks.length,dependencies:dependencyEdges.length},
    inventory:checks.inventory.ndjson,dashboard:checks.dashboard.ndjson,atomic:checks.atomic.ndjson,errors:checks.errors.ndjson
  },null,2));

  // Render every worksheet for visual QA. Long registers render in logical slices.
  const renderJobs=[
    ["Start Here","A1:J23","start_here.png"],["Dashboard","A1:N31","dashboard.png"],["Phases",`A1:O${phaseInfo.endRow}`,"phases.png"],
    ["Work Items","A1:M28","work_items_a.png"],["Work Items",`N1:W28`,"work_items_b.png"],
    ["Atomic Steps","A1:M30","atomic_steps_a.png"],["Atomic Steps","N1:V30","atomic_steps_b.png"],
    ["Decisions",`A1:K${decisionInfo.endRow}`,"decisions.png"],["Gate Criteria","A1:J28","gate_criteria_a.png"],["Gate Criteria",`A${Math.max(29,gateInfo.endRow-20)}:J${gateInfo.endRow}`,"gate_criteria_b.png"],
    ["Dependencies","A1:G30","dependencies.png"],["Requirements Traceability","A1:I30","requirements_a.png"],["Requirements Traceability",`A${Math.max(31,reqInfo.endRow-20)}:I${reqInfo.endRow}`,"requirements_b.png"],
    ["Artifacts",`A1:H${artInfo.endRow}`,"artifacts.png"],["Risks",`A1:I${riskInfo.endRow}`,"risks.png"],["Evidence","A1:K16","evidence.png"],["Progress Contract",`A1:E${row+algorithm.length+2}`,"progress_contract.png"],
    ["Agent Sessions","A1:O15","agent_sessions.png"],["Plan Changes","A1:N15","plan_changes.png"],["Reference Lists",`A1:F${4+maxLen}`,"reference_lists.png"]
  ];
  const previewDir=path.join(CONFIG.outputDir,"workbook_previews"); await fs.mkdir(previewDir,{recursive:true});
  for(const [sheetName,range,file] of renderJobs){
    const blob=await wb.render({sheetName,range,scale:1,format:"png"});
    await fs.writeFile(path.join(previewDir,file),new Uint8Array(await blob.arrayBuffer()));
  }
  console.log(JSON.stringify({outputPath,counts:{tasks:tasks.length,atomic:atomic.length,decisions:decisions.length,gates:gates.length,requirements:requirements.length,risks:risks.length,dependencies:dependencyEdges.length},previewDir},null,2));
}

main().catch(error=>{console.error(error);process.exit(1);});
