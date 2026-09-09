import unittest
from pathlib import Path
from tools.progress.schema import ValidationError, validate


class ConditionalSchemaTests(unittest.TestCase):
    def test_validity_selects_required_pose_or_null_branch(self):
        schema = {"if": {"properties": {"valid": {"const": True}}, "required": ["valid"]},
                  "then": {"properties": {"pose": {"type": "object"}}, "required": ["pose"]},
                  "else": {"properties": {"pose": {"type": "null"}}, "required": ["pose"]}}
        for value in ({"valid": True, "pose": {}}, {"valid": False, "pose": None}):
            validate(value, schema, base=Path("."))
        for value in ({"valid": True, "pose": None}, {"valid": False, "pose": {}}, {"valid": True}):
            with self.assertRaises(ValidationError): validate(value, schema, base=Path("."))

    def test_property_names_reject_unicode_and_overlength(self):
        schema = {"propertyNames": {"pattern": "^[A-Za-z0-9_.-]+$", "maxLength": 4}}
        validate({"key": 1}, schema, base=Path("."))
        for key in ("é", "abcde", "bad key"):
            with self.assertRaises(ValidationError): validate({key: 1}, schema, base=Path("."))

    def test_then_and_else_without_if_do_not_apply(self):
        validate(1, {"then": False, "else": False}, base=Path("."))

    def test_unsupported_assertion_is_not_a_false_condition_or_ignored_alternative(self):
        for schema in ({"if": {"unsupported": True}, "else": True},
                       {"anyOf": [True, {"unsupported": True}]}):
            with self.assertRaisesRegex(ValidationError, "Unsupported"):
                validate(1, schema, base=Path("."))


if __name__ == "__main__": unittest.main()
