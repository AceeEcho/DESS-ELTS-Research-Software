import hashlib, json, tempfile, unittest
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).parents[1] / 'src'))
from elts_analysis.ingest import BLOCK_TICKS, IngestError, ingest_run

class IngestTests(unittest.TestCase):
    def make_run(self, bad_schema=False, duplicate=False):
        root=Path(tempfile.mkdtemp()); run=root/'run'; run.mkdir()
        def sample(seq,tick,valid=True):
            tracker=lambda ident: {'trackerId':ident,'connection':'Connected','validity':'Valid','pose':{'positionMeters':{'x':0,'y':0,'z':0},'orientation':{'x':0,'y':0,'z':0,'w':1}}} if valid else {'trackerId':ident,'connection':'Disconnected','validity':'Unavailable','pose':None}
            return {'schemaVersion':'bad.v9' if bad_schema else 'elts.samples.v1','sequence':seq,'monotonicTicks':tick,'head':tracker('HEAD'),'weapon':tracker('WEAPON')}
        samples=[sample(1,1),sample(2,2,False)]
        events=[{'schemaVersion':'elts.events.v1','sequence':1,'monotonicTicks':0,'eventType':'BlockStarted','payload':{'blockId':'b1'}},{'schemaVersion':'elts.events.v1','sequence':2,'monotonicTicks':3,'eventType':'TargetDestroyed','payload':{'blockId':'b1','targetId':'t1'}},{'schemaVersion':'elts.events.v1','sequence':3,'monotonicTicks':4,'eventType':'TargetDestroyed','payload':{'blockId':'b1','targetId':'t1'}},{'schemaVersion':'elts.events.v1','sequence':4,'monotonicTicks':BLOCK_TICKS,'eventType':'BlockEnded','payload':{'blockId':'b1'}}] if duplicate else [{'schemaVersion':'elts.events.v1','sequence':1,'monotonicTicks':0,'eventType':'BlockStarted','payload':{'blockId':'b1'}},{'schemaVersion':'elts.events.v1','sequence':2,'monotonicTicks':3,'eventType':'ShotFired','payload':{}},{'schemaVersion':'elts.events.v1','sequence':3,'monotonicTicks':4,'eventType':'TargetDestroyed','payload':{'blockId':'b1','targetId':'t1'}},{'schemaVersion':'elts.events.v1','sequence':4,'monotonicTicks':BLOCK_TICKS,'eventType':'BlockEnded','payload':{'blockId':'b1'}}]
        targets=[{'schemaVersion':'elts.targets.v1','sequence':1,'monotonicTicks':0,'targetId':'t1','blockId':'b1','lifecycle':'Spawned','worldPositionMeters':{'x':0,'y':0,'z':1},'worldVelocityMetersPerSecond':{'x':0,'y':0,'z':0},'scenarioSeed':1,'scenarioVersion':'fixture'}]
        for name, rows in [('samples.ndjson',samples),('events.ndjson',events),('targets.ndjson',targets)]: (run/name).write_text(''.join(json.dumps(x,separators=(',',':'))+'\n' for x in rows),encoding='utf-8')
        sums={name:hashlib.sha256((run/name).read_bytes()).hexdigest() for name in ('samples.ndjson','events.ndjson','targets.ndjson')}
        summary={'schemaVersion':'elts.session-summary.v1','runId':'fixture','complete':True,'error':None,'provenance':{'applicationVersion':'test','sourceRevision':'abc','configurationHash':'a'*64,'scenarioHash':'b'*64,'fixtureHash':'c'*64,'synthetic':True,'utcStartupAnchor':'2026-09-06T00:00:00+00:00'},'counts':{'droppedSamples':0,'writtenSamples':2,'writtenEvents':len(events),'writtenTargets':1},'checksumsSha256':sums}
        (run/'session-summary.json').write_text(json.dumps(summary),encoding='utf-8')
        cal=root/'cal.json'; cal.write_text(json.dumps({'calibrationId':'synthetic-fixture','muzzleOffsetMeters':[0,0,0],'boreDirectionLocal':[0,0,1],'zeroCorrectionQuaternion':[0,0,0,1]}),encoding='utf-8')
        return root,run,cal
    def test_counts_and_invalid_exclusion(self):
        root,run,cal=self.make_run(); self.addCleanup(lambda: __import__('shutil').rmtree(root,ignore_errors=True)); result=ingest_run(run,cal)
        self.assertEqual(result['counts']['invalidSamples'],1); self.assertEqual(result['blocks'][0]['targetsDestroyed'],1); self.assertEqual(result['blocks'][0]['shots'],1); self.assertEqual(result['blocks'][0]['validSamples'],1); self.assertEqual(result['blocks'][0]['meanAimErrorDegrees'],0.0); self.assertEqual(result['source']['rawInputs']['samples.ndjson']['sha256'],hashlib.sha256((run/'samples.ndjson').read_bytes()).hexdigest())
    def test_unsupported_schema(self):
        root,run,cal=self.make_run(bad_schema=True); self.addCleanup(lambda: __import__('shutil').rmtree(root,ignore_errors=True));
        with self.assertRaisesRegex(IngestError,'unsupported schema'): ingest_run(run,cal)
    def test_duplicate_score_rejected(self):
        root,run,cal=self.make_run(duplicate=True); self.addCleanup(lambda: __import__('shutil').rmtree(root,ignore_errors=True));
        with self.assertRaisesRegex(IngestError,'duplicate TargetDestroyed'): ingest_run(run,cal)
    def test_missing_markers_score_unavailable(self):
        root,run,cal=self.make_run(); self.addCleanup(lambda: __import__('shutil').rmtree(root,ignore_errors=True));
        events=[line for line in (run/'events.ndjson').read_text().splitlines() if 'TargetDestroyed' not in line and 'BlockEnded' not in line]; (run/'events.ndjson').write_text('\n'.join(events)+'\n');
        # Rebuild the summary checksum and count after removing BlockEnded.
        summary=json.loads((run/'session-summary.json').read_text()); summary['counts']['writtenEvents']=len(events); summary['checksumsSha256']['events.ndjson']=hashlib.sha256((run/'events.ndjson').read_bytes()).hexdigest(); (run/'session-summary.json').write_text(json.dumps(summary));
        result=ingest_run(run,cal); self.assertEqual(result['blocks'][0]['scoreStatus'],'unavailable')

    def test_partial_block_cannot_complete_score(self):
        root,run,cal=self.make_run(); self.addCleanup(lambda: __import__('shutil').rmtree(root,ignore_errors=True))
        events=[json.loads(line) for line in (run/'events.ndjson').read_text().splitlines() if 'BlockEnded' not in line]
        (run/'events.ndjson').write_text(''.join(json.dumps(x,separators=(',',':'))+'\n' for x in events))
        summary=json.loads((run/'session-summary.json').read_text()); summary['counts']['writtenEvents']=len(events); summary['checksumsSha256']['events.ndjson']=hashlib.sha256((run/'events.ndjson').read_bytes()).hexdigest(); (run/'session-summary.json').write_text(json.dumps(summary))
        result=ingest_run(run,cal); self.assertEqual(result['blocks'][0]['scoreStatus'],'unavailable')

    def test_summary_count_mismatch_rejected(self):
        root,run,cal=self.make_run(); self.addCleanup(lambda: __import__('shutil').rmtree(root,ignore_errors=True))
        summary=json.loads((run/'session-summary.json').read_text()); summary['counts']['writtenSamples']=999; (run/'session-summary.json').write_text(json.dumps(summary))
        with self.assertRaisesRegex(IngestError,'writtenSamples'):
            ingest_run(run,cal)
if __name__=='__main__': unittest.main()


