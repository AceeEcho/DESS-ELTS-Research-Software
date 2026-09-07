"""Journal import validates all receipts before publishing and can resume."""
import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path

from import_journal import BOOTSTRAP_IDS, import_journal
from schema import ValidationError
from store import generate, load_events
from test_core import History, fixture_root


class JournalTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="ELTS journal fixture ")
        self.root = Path(self.tmp.name) / "isolated project"
        fixture_root(self.root)
        history = History(self.root)
        folder = self.root / "project-management/bootstrap"
        folder.mkdir()
        entries = [{"schemaVersion": 1, "recordedAtUtc": "2026-01-01T00:00:00Z", "occurredAtUtc": "2026-01-01T00:00:00Z",
                    "actor": "agent:codex-astra", "stepId": key, "branch": "bootstrap/repository-governance",
                    "ownedPaths": ["tools/progress/"], "work": "UNIT TEST journal observation only",
                    "checks": ["isolated fixture"], "evidence": ["fixture-evidence.json"], "result": "verified_observation"}
                   for key in BOOTSTRAP_IDS]
        (folder / "journal.jsonl").write_text("".join(json.dumps(e) + "\n" for e in entries), encoding="utf-8")
        self.index = {key: [history.evidence(key)] for key in BOOTSTRAP_IDS}
        self.index_path = folder / "evidence-index.json"
        self.index_path.write_text(json.dumps(self.index), encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def test_bad_last_receipt_writes_nothing(self):
        self.index[BOOTSTRAP_IDS[-1]][0]["sha256"] = "0" * 64
        self.index_path.write_text(json.dumps(self.index), encoding="utf-8")
        with self.assertRaisesRegex(ValidationError, "checksum"):
            import_journal(self.root, write=True)
        self.assertEqual(load_events(self.root), [])

    def test_import_provenance_and_repeat_noop(self):
        with contextlib.redirect_stdout(io.StringIO()):
            import_journal(self.root, write=True)
            before = (self.root / "PROJECT_STATE.json").read_bytes()
            import_journal(self.root, write=True)
        events = load_events(self.root)
        self.assertEqual(len(events), 30)
        self.assertTrue(all(e["recordedAtUtc"] > e["occurredAtUtc"] for e in events))
        self.assertTrue(all(e["note"].startswith("PC-001 journal ") for e in events))
        self.assertEqual((self.root / "PROJECT_STATE.json").read_bytes(), before)
        self.assertEqual(generate(self.root)["execution"]["primaryStepId"], "BOOT.S011")


if __name__ == "__main__":
    unittest.main()
