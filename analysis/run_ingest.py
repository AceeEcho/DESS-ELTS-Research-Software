"""Portable source checkout entry point for the ELTS analysis ingest."""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "analysis" / "src"
if str(SOURCE) not in sys.path:
    sys.path.insert(0, str(SOURCE))

from elts_analysis.ingest import main

raise SystemExit(main())
