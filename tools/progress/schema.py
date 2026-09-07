"""Fail-closed validator for the JSON Schema vocabulary used by ELTS tools.

This intentionally implements a documented subset, not a general JSON Schema
engine. Unsupported assertion keywords are errors, never silently ignored.
All schemas are local; validation performs no network access. Python 3.10+.
"""
from __future__ import annotations

import json
import math
import re
from datetime import datetime
from pathlib import Path


class ValidationError(ValueError):
    """A precise, operator-readable contract violation."""


ANNOTATIONS = {"$schema", "$id", "title", "description", "$comment", "default", "examples"}
ASSERTIONS = {"$ref", "$defs", "type", "properties", "required", "additionalProperties",
              "items", "minItems", "maxItems", "uniqueItems", "enum", "const", "pattern",
              "minLength", "maxLength", "minimum", "maximum", "exclusiveMinimum",
              "oneOf", "anyOf", "allOf", "format", "minProperties"}


def load_json(path: Path):
    """Reject duplicate keys and non-JSON numeric constants (NaN/Infinity)."""
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValidationError(f"Duplicate JSON key: {key}")
            result[key] = value
        return result

    def invalid(value):
        raise ValidationError(f"Non-finite JSON number: {value}")

    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=pairs,
                      parse_constant=invalid)


def validate(value, schema, *, base: Path, document=None, location="$", depth=0):
    if depth > 100:
        raise ValidationError(f"{location}: schema nesting exceeds 100")
    if schema is True:
        return
    if schema is False:
        raise ValidationError(f"{location}: value prohibited")
    if not isinstance(schema, dict):
        raise ValidationError(f"{location}: malformed schema")
    unknown = set(schema) - ANNOTATIONS - ASSERTIONS
    if unknown:
        raise ValidationError(f"Unsupported schema keywords: {sorted(unknown)}")
    document = schema if document is None else document

    def check(item, rule, path=location):
        validate(item, rule, base=base, document=document, location=path, depth=depth + 1)

    if "$ref" in schema:
        ref = schema["$ref"]
        filename, _, fragment = ref.partition("#")
        if filename:
            if ":" in filename or "\\" in filename or Path(filename).is_absolute():
                raise ValidationError("Only repository-local schema references are supported")
            target = (base / filename).resolve()
            # Schemas may reference a sibling, never an arbitrary local file.
            if target.parent != base.resolve():
                raise ValidationError("Schema reference escapes its schema directory")
            referenced = load_json(target)
            ref_base = target.parent
        else:
            referenced, ref_base = document, base
        rule = referenced
        for token in fragment.lstrip("/").split("/") if fragment else []:
            rule = rule[token.replace("~1", "/").replace("~0", "~")]
        validate(value, rule, base=ref_base, document=referenced, location=location, depth=depth + 1)
    types = schema.get("type")
    if types:
        choices = types if isinstance(types, list) else [types]
        matches = {"null": value is None, "boolean": type(value) is bool,
                   "integer": type(value) is int,
                   "number": type(value) in (int, float) and math.isfinite(value),
                   "string": isinstance(value, str), "array": isinstance(value, list),
                   "object": isinstance(value, dict)}
        if not any(matches.get(t, False) for t in choices):
            raise ValidationError(f"{location}: expected {choices}, got {type(value).__name__}")
    if "const" in schema and (value != schema["const"] or type(value) != type(schema["const"])):
        raise ValidationError(f"{location}: expected constant {schema['const']!r}")
    if "enum" in schema and not any(value == v and type(value) == type(v) for v in schema["enum"]):
        raise ValidationError(f"{location}: value is outside the allowed enum")
    for keyword in ("allOf", "anyOf", "oneOf"):
        if keyword not in schema:
            continue
        passed = 0
        for rule in schema[keyword]:
            try:
                check(value, rule)
                passed += 1
            except ValidationError:
                pass
        expected = len(schema[keyword]) if keyword == "allOf" else 1
        if (passed != expected if keyword in ("allOf", "oneOf") else passed < expected):
            raise ValidationError(f"{location}: failed {keyword}")
    if isinstance(value, dict):
        missing = set(schema.get("required", [])) - set(value)
        if missing:
            raise ValidationError(f"{location}: missing fields {sorted(missing)}")
        if len(value) < schema.get("minProperties", 0):
            raise ValidationError(f"{location}: too few properties")
        properties = schema.get("properties", {})
        for key, item in value.items():
            check(item, properties.get(key, schema.get("additionalProperties", True)), f"{location}.{key}")
    if isinstance(value, list):
        if not schema.get("minItems", 0) <= len(value) <= schema.get("maxItems", math.inf):
            raise ValidationError(f"{location}: invalid array length")
        if schema.get("uniqueItems"):
            serialized = [json.dumps(x, sort_keys=True, allow_nan=False) for x in value]
            if len(set(serialized)) != len(value):
                raise ValidationError(f"{location}: duplicate array items")
        for i, item in enumerate(value):
            check(item, schema.get("items", True), f"{location}[{i}]")
    if isinstance(value, str):
        if not schema.get("minLength", 0) <= len(value) <= schema.get("maxLength", math.inf):
            raise ValidationError(f"{location}: invalid string length")
        if "pattern" in schema and not re.search(schema["pattern"], value):
            raise ValidationError(f"{location}: invalid string format")
        if schema.get("format") == "date-time":
            try:
                parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
                if parsed.tzinfo is None or parsed.utcoffset().total_seconds() != 0:
                    raise ValueError("UTC offset required")
            except ValueError as exc:
                raise ValidationError(f"{location}: expected UTC date-time") from exc
        elif "format" in schema:
            raise ValidationError(f"Unsupported format: {schema['format']}")
    if type(value) in (int, float):
        if not math.isfinite(value):
            raise ValidationError(f"{location}: number must be finite")
        if not schema.get("minimum", -math.inf) <= value <= schema.get("maximum", math.inf):
            raise ValidationError(f"{location}: number outside permitted range")
        if "exclusiveMinimum" in schema and value <= schema["exclusiveMinimum"]:
            raise ValidationError(f"{location}: number must exceed minimum")


def validate_file(value, schema_path: Path):
    validate(value, load_json(schema_path), base=schema_path.parent)
