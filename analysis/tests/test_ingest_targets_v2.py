"""Regression: an expiring v2 target is removed from aim and never scores a hit."""
from __future__ import annotations
import hashlib, json, tempfile, unittest
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).parents[1] / 'src'))
from elts_analysis.ingest import ingest_run

HASH = "a" * 64
def line(value): return json.dumps(value, separators=(",", ":")) + "\n"
def digest(value): return hashlib.sha256(value).hexdigest()

class TargetV2IngestTests(unittest.TestCase):
    def test_despawned_target_is_not_an_aim_candidate_or_score(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            pose = {"positionMeters":{"x":0,"y":0,"z":0},"orientation":{"x":0,"y":0,"z":0,"w":1}}
            sample = line({"schemaVersion":"elts.samples.v1","sequence":1,"monotonicTicks":2,"head":{"trackerId":"head","connection":"Connected","validity":"Valid","pose":pose},"weapon":{"trackerId":"weapon","connection":"Connected","validity":"Valid","pose":pose}})
            events = line({"schemaVersion":"elts.events.v1","sequence":1,"monotonicTicks":0,"eventType":"BlockStarted","payload":{"blockId":"block"}}) + line({"schemaVersion":"elts.events.v1","sequence":2,"monotonicTicks":3000000000,"eventType":"BlockEnded","payload":{"blockId":"block"}})
            targets = line({"schemaVersion":"elts.targets.v2","sequence":1,"monotonicTicks":0,"targetId":"target","blockId":"block","lifecycle":"Spawned","worldPositionMeters":{"x":0,"y":0,"z":1},"worldVelocityMetersPerSecond":{"x":0,"y":0,"z":0},"scenarioSeed":1,"scenarioVersion":"v2"}) + line({"schemaVersion":"elts.targets.v2","sequence":2,"monotonicTicks":1,"targetId":"target","blockId":"block","lifecycle":"Despawned","worldPositionMeters":{"x":0,"y":0,"z":1},"worldVelocityMetersPerSecond":{"x":0,"y":0,"z":0},"scenarioSeed":1,"scenarioVersion":"v2"})
            for name, value in (("samples.ndjson", sample), ("events.ndjson", events), ("targets.ndjson", targets)): (root / name).write_text(value, encoding="utf-8")
            summary = {"schemaVersion":"elts.session-summary.v1","runId":"v2expiry","complete":True,"error":None,"provenance":{"applicationVersion":"test","sourceRevision":"test","configurationHash":HASH,"scenarioHash":HASH,"fixtureHash":HASH,"synthetic":True,"utcStartupAnchor":"2026-09-08T00:00:00Z"},"counts":{"droppedSamples":0,"writtenSamples":1,"writtenEvents":2,"writtenTargets":2},"checksumsSha256":{name:digest((root/name).read_bytes()) for name in ("samples.ndjson","events.ndjson","targets.ndjson")}}
            (root / "session-summary.json").write_text(json.dumps(summary), encoding="utf-8")
            calibration = root / "calibration.json"; calibration.write_text(json.dumps({"calibrationId":"synthetic","muzzleOffsetMeters":[0,0,0],"boreDirectionLocal":[0,0,1],"zeroCorrectionQuaternion":[0,0,0,1]}), encoding="utf-8")
            report = ingest_run(root, calibration)
            self.assertEqual(report["blocks"][0]["targetsDestroyed"], 0)
            self.assertEqual(report["blocks"][0]["validSamples"], 1)
            self.assertIsNone(report["blocks"][0]["meanAimErrorDegrees"])

if __name__ == "__main__": unittest.main()
