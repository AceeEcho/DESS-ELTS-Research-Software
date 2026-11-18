"""Copy and restore only disposable fictional station data."""
from contextlib import closing
import json
import os
from pathlib import Path
import sqlite3
import tempfile
import unittest

from tools.data.station_copy import COPY_INFO, exclusive, station_copy


class StationCopyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="elts-copy-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.station = self.root / "station"
        self.data = self.station / "collection data"
        self.profiles = self.station / "Calibration Environment/profiles"
        self.profiles.mkdir(parents=True)
        for run in ("session-A", "excluded-B"):
            directory = self.data / run
            directory.mkdir(parents=True)
            (directory / "events.ndjson").write_bytes(b'{"fixture":true}\n')
            (self.data / ("." + run + ".reservation")).touch()
        (self.data / "exports").mkdir()
        (self.data / "exports/results.xlsx").write_bytes(b"fictional export bytes")
        (self.profiles / "participant-42.json").write_text('{"participantId":42}', encoding="utf-8")
        (self.profiles.parent / "station-room.json").write_text('{"version":1}', encoding="utf-8")
        (self.profiles / ".dataset-generation").write_bytes(b"fictional-generation")
        with closing(sqlite3.connect(self.data / "collection.sqlite")) as db:
            db.executescript("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE participants(id TEXT PRIMARY KEY);
                INSERT INTO participants VALUES ('profile-A');
                CREATE TABLE sessions(id TEXT PRIMARY KEY, participantId TEXT REFERENCES participants,
                                      visitId TEXT, directory TEXT UNIQUE);
                CREATE TABLE removed_recordings(directory TEXT PRIMARY KEY);
            """)
            with db:
                db.execute("INSERT INTO sessions VALUES (?,?,?,?)", ("stable-id", "profile-A", "visit-A", str(self.data / "session-A")))
                db.execute("INSERT INTO removed_recordings VALUES (?)", (str(self.data / "excluded-B"),))

    def test_preview_is_read_only_and_destination_must_be_new_and_external(self):
        destination = self.root / "new station"
        self.assertEqual(station_copy(self.station, destination), 8)
        self.assertFalse(destination.exists())
        self.assertFalse((self.data / ".participant-store.lock").exists())
        for forbidden in (self.station, self.data / "copy", self.root, self.root / "absent" / "copy"):
            with self.assertRaises(ValueError):
                station_copy(self.station, forbidden)
        destination.mkdir()
        (destination / "keep.txt").write_text("unrelated", encoding="ascii")
        with self.assertRaises(ValueError):
            station_copy(self.station, destination)
        if os.name == "nt":
            with self.assertRaises(ValueError):
                station_copy(self.station, destination, apply=True)
        else:
            with self.assertRaisesRegex(RuntimeError, "Windows station"):
                station_copy(self.station, destination, apply=True)
        self.assertEqual((destination / "keep.txt").read_text(), "unrelated")

    def test_preview_uses_station_configuration_and_detects_ambiguous_legacy_roots(self):
        nested = self.station / "data/synthetic"
        nested.mkdir(parents=True)
        destination = self.root / "backup"
        with self.assertRaisesRegex(ValueError, "Multiple recording directories"):
            station_copy(self.station, destination)
        (self.station / "config").mkdir()
        (self.station / "config/local.json").write_text('{"dataRoot":"collection data"}', encoding="utf-8")
        self.assertEqual(station_copy(self.station, destination), 8)

    @unittest.skipUnless(os.name == "nt", "Windows station and calibration leases")
    def test_transported_copy_restores_without_changing_visit_or_exclusion(self):
        backup = self.root / "backup"
        self.assertEqual(station_copy(self.station, backup, apply=True), 8)
        transported = self.root / "copy moved to another location"
        backup.rename(transported)
        restored = self.root / "restored station"
        self.assertEqual(station_copy(transported, restored, apply=True), 8)
        for location in (self.station, restored):
            data = location / "collection data"
            with closing(sqlite3.connect(data / "collection.sqlite")) as db:
                self.assertEqual(db.execute("PRAGMA integrity_check").fetchall(), [("ok",)])
                self.assertEqual(db.execute("PRAGMA foreign_key_check").fetchall(), [])
                self.assertEqual(db.execute("SELECT * FROM sessions").fetchone(),
                                 ("stable-id", "profile-A", "visit-A", str(data / "session-A")))
                self.assertEqual(db.execute("SELECT directory FROM removed_recordings").fetchone(), (str(data / "excluded-B"),))
            for path in ("session-A/events.ndjson", "excluded-B/events.ndjson", ".session-A.reservation", "exports/results.xlsx"):
                self.assertEqual((data / path).read_bytes(), (self.data / path).read_bytes())
            for path in ("profiles/.dataset-generation", "profiles/participant-42.json", "station-room.json"):
                self.assertEqual((location / "Calibration Environment" / path).read_bytes(), (self.profiles.parent / path).read_bytes())
        self.assertEqual(set(json.loads((restored / COPY_INFO).read_text())), {"version", "dataRoot", "indexedRoot"})

    @unittest.skipUnless(os.name == "nt", "Windows station and calibration leases")
    def test_active_lease_or_invalid_index_never_publishes_a_partial_copy(self):
        target = self.root / "copy"
        for lock in (self.data / ".participant-store.lock", self.profiles / ".dataset-generation"):
            with exclusive(lock), self.assertRaises(OSError):
                station_copy(self.station, target, apply=True)
            self.assertFalse(target.exists())
        with closing(sqlite3.connect(self.data / "collection.sqlite")) as db:
            with db:
                db.execute("UPDATE removed_recordings SET directory=?", (str(self.root / "unrelated-run"),))
        with self.assertRaisesRegex(ValueError, "outside the selected data folder"):
            station_copy(self.station, target, apply=True)
        self.assertFalse(target.exists())
        self.assertFalse(list(self.root.glob("elts-station-copy-*")))


if __name__ == "__main__":
    unittest.main()
