"""Negative fixtures for the local schema vocabulary and strict JSON input."""
import json
import tempfile
import unittest
from pathlib import Path

from schema import ValidationError, load_json, validate


class SchemaTests(unittest.TestCase):
    def check(self, value, rule):
        validate(value, rule, base=Path(__file__).parent)

    def test_strict_objects_and_types(self):
        rule = {"type": "object", "properties": {"n": {"type": "integer", "minimum": 0}},
                "required": ["n"], "additionalProperties": False}
        self.check({"n": 1}, rule)
        for value in ({}, {"n": True}, {"n": -1}, {"n": 0, "typo": 2}):
            with self.subTest(value=value), self.assertRaises(ValidationError):
                self.check(value, rule)

    def test_unsupported_assertions_fail(self):
        with self.assertRaisesRegex(ValidationError, "Unsupported"):
            self.check("ignored?", {"typ": "integer"})

    def test_reference_composition_and_uniqueness(self):
        self.check([1, 2], {"type": "array", "items": {"$ref": "#/$defs/n"},
                           "$defs": {"n": {"type": "integer"}}, "uniqueItems": True})
        for value in ([1, True], [1, 1]):
            with self.assertRaises(ValidationError):
                self.check(value, {"type": "array", "items": {"type": "integer"}, "uniqueItems": True})

    def test_utc_and_path_constraints(self):
        from build_schemas import PATH, TIME
        for good in ("docs/a.md", "project-management/progress/evidence/one.json"):
            self.check(good, PATH)
        for bad in ("../x", "a/../b", "/absolute", "C:/x", "a\\b"):
            with self.assertRaises(ValidationError):
                self.check(bad, PATH)
        self.check("2026-09-07T00:00:00Z", TIME)
        for bad in ("2026-09-07T00:00:00", "2026-09-07T01:00:00+01:00", "yesterday"):
            with self.assertRaises(ValidationError):
                self.check(bad, TIME)

    def test_duplicate_keys_and_nonfinite_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "input.json"
            for text in ('{"id":1,"id":2}', '{"n":NaN}', '{"n":Infinity}'):
                path.write_text(text, encoding="utf-8")
                with self.assertRaises(ValidationError):
                    load_json(path)


if __name__ == "__main__":
    unittest.main()
