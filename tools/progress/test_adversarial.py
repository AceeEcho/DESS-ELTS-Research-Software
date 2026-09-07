"""Regression fixtures for cross-actor mutations of an owned task chain.

Every fixture root is temporary.  These tests never write production events or
generated state.
"""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from schema import ValidationError
from test_core import History, fixture_root


class TaskOwnershipAdversarialTests(unittest.TestCase):
    def test_non_owner_cannot_block_ready_sibling_of_owned_task(self):
        """A task owner must protect every child transition, not just its claim."""
        with tempfile.TemporaryDirectory(prefix="ELTS ownership fixture ") as temporary:
            root = Path(temporary) / "isolated project"
            fixture_root(root)
            history = History(root)
            history.done("P0.3.S001")

            other = {"type": "agent", "id": "other-agent", "tool": "adversarial-fixture"}
            blocker = [{"id": "EXT-injected", "reason": "attempted non-owner mutation",
                        "requiredAction": "Task owner must assess the blocker",
                        "responsibleRole": "task owner"}]
            with self.assertRaisesRegex(ValidationError, "current owner of task P0.3"):
                history.add("BOOT.S002", "blocked", "blocked", actor=other, blockers=blocker)


if __name__ == "__main__":
    unittest.main()
