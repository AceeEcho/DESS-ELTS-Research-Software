"""Read-only analysis ingest for ELTS logging v1 synthetic recordings."""
from .ingest import IngestError, ingest_run

__all__ = ["IngestError", "ingest_run"]
