# Planning catalog importer

`tools/progress/import_plan.py` implements BOOT.S006's bounded catalog import.
It calls the approved plan builder, verifies both deliverable hashes and the
immutable copies under `project-management/baseline/`, and validates the
result with `schemas/progress/task-catalog.schema.json`.

Run from any directory with the bundled Python 3.10+ runtime:

```text
python tools/progress/import_plan.py --write
python tools/progress/import_plan.py --check
```

`--write` emits `project-management/exports/task-catalog.json` plus one UTF-8,
LF-normalized JSON and CSV pair for every workbook sheet. Formula cells are
objects containing `formula` and `cachedValue`, so cached values never replace
the source expression. `--check` computes the same bytes and reports drift
without writing. Workbook status columns remain source provenance and do not
become live progress.
