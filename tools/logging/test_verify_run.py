"""Focused read-only verification tests for ELTS logging v1."""
from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from tools.logging.verify_run import VerificationError, verify_run


HASH = "a" * 64


def _sample() -> dict:
    return {
        "schemaVersion": "elts.samples.v1", "sequence": 1, "monotonicTicks": 4,
        "head": {"trackerId": "HEAD-001", "connection": "Connected", "validity": "Valid",
                 "pose": {"positionMeters": {"x": 1, "y": 2, "z": 3}, "orientation": {"x": 0, "y": 0, "z": 0, "w": 1}}},
        "weapon": {"trackerId": "WEAPON-001", "connection": "Connected", "validity": "OutOfRange", "pose": None},
    }


def _event() -> dict:
    return {"schemaVersion": "elts.events.v1", "sequence": 1, "monotonicTicks": 4, "eventType": "synthetic-event", "payload": {"enabled": True}}


def _target() -> dict:
    return {"schemaVersion": "elts.targets.v1", "sequence": 1, "monotonicTicks": 4, "targetId": "target-1", "blockId": "block-1", "lifecycle": "Spawned", "worldPositionMeters": {"x": 1, "y": 2, "z": 3}, "worldVelocityMetersPerSecond": {"x": 0, "y": 0, "z": 1}, "scenarioSeed": 1, "scenarioVersion": "synthetic-v1"}


def _write_json(path: Path, value: dict, newline: bool = False) -> None:
    path.write_text(json.dumps(value, separators=(",", ":")) + ("\n" if newline else ""), encoding="utf-8")


def _refresh_summary(directory: Path, *, complete: bool = True, counts: dict | None = None) -> None:
    checksums = {name: hashlib.sha256((directory / name).read_bytes()).hexdigest() for name in ("samples.ndjson", "events.ndjson", "targets.ndjson")}
    _write_json(directory / "session-summary.json", {
        "schemaVersion": "elts.session-summary.v1", "runId": "synthetic-run", "complete": complete, "error": None,
        "provenance": {"applicationVersion": "test", "sourceRevision": "test-revision", "configurationHash": HASH, "scenarioHash": HASH, "fixtureHash": HASH, "synthetic": True, "utcStartupAnchor": "2026-09-06T12:00:00+00:00"},
        "counts": counts or {"droppedSamples": 0, "writtenSamples": 1, "writtenEvents": 1, "writtenTargets": 1},
        "checksumsSha256": checksums,
    })


def _valid_run(directory: Path) -> None:
    _write_json(directory / "samples.ndjson", _sample(), newline=True)
    _write_json(directory / "events.ndjson", _event(), newline=True)
    _write_json(directory / "targets.ndjson", _target(), newline=True)
    _refresh_summary(directory)


class VerifyRunTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)
        _valid_run(self.directory)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def test_valid_complete_run_produces_compact_counts(self) -> None:
        report = verify_run(self.directory)
        self.assertTrue(report["complete"])
        self.assertEqual("test-revision", report["sourceRevision"])
        self.assertEqual([1, 1, 1], [item["lines"] for item in report["products"]])

    def test_duplicate_and_nonfinite_json_are_rejected_before_summary_trust(self) -> None:
        for raw in (
            '{"schemaVersion":"elts.events.v1","sequence":1,"sequence":2}',
            '{"schemaVersion":"elts.events.v1","sequence":NaN}',
        ):
            with self.subTest(raw=raw):
                (self.directory / "events.ndjson").write_text(raw + "\n", encoding="utf-8")
                with self.assertRaisesRegex(VerificationError, "Duplicate JSON key|Non-finite"):
                    verify_run(self.directory)
                _write_json(self.directory / "events.ndjson", _event(), newline=True)

    def test_checksum_count_order_and_pose_tampering_are_rejected(self) -> None:
        sample = _sample()
        sample["weapon"]["pose"] = {"positionMeters": {"x": 0, "y": 0, "z": 0}, "orientation": {"x": 0, "y": 0, "z": 0, "w": 1}}
        _write_json(self.directory / "samples.ndjson", sample, newline=True)
        _refresh_summary(self.directory)
        with self.assertRaisesRegex(VerificationError, "invalid tracking|failed allOf"):
            verify_run(self.directory)

        _valid_run(self.directory)
        target_path = self.directory / "targets.ndjson"
        target_path.write_text(target_path.read_text(encoding="utf-8").replace("target-1", "target-x"), encoding="utf-8")
        with self.assertRaisesRegex(VerificationError, "SHA-256"):
            verify_run(self.directory)

        _valid_run(self.directory)
        _refresh_summary(self.directory, counts={"droppedSamples": 0, "writtenSamples": 2, "writtenEvents": 1, "writtenTargets": 1})
        with self.assertRaisesRegex(VerificationError, "lines but summary reports"):
            verify_run(self.directory)

    def test_per_stream_sequence_and_timestamp_must_remain_monotonic(self) -> None:
        first = _sample()
        second = _sample()
        second["sequence"] = 2
        second["monotonicTicks"] = 3
        (self.directory / "samples.ndjson").write_text(json.dumps(first, separators=(",", ":")) + "\n" + json.dumps(second, separators=(",", ":")) + "\n", encoding="utf-8")
        _refresh_summary(self.directory, counts={"droppedSamples": 0, "writtenSamples": 2, "writtenEvents": 1, "writtenTargets": 1})
        with self.assertRaisesRegex(VerificationError, "monotonicTicks moved backward"):
            verify_run(self.directory)
    def test_incomplete_summary_is_rejected(self) -> None:
        _refresh_summary(self.directory, complete=False)
        with self.assertRaisesRegex(VerificationError, "complete successful"):
            verify_run(self.directory)


if __name__ == "__main__":
    unittest.main()
