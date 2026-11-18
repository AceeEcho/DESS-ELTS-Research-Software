"""Software-only API, input-boundary and tracking validity checks."""
import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import server
from tracking import checked_matrix, TrackingBridge, TriggerState


class ConfigTests(unittest.TestCase):
    def test_trigger_hold_and_disconnect(self):
        trigger = TriggerState()
        self.assertEqual(trigger.observe(True, True)["presses"], 0)
        trigger.observe(True, False)
        self.assertEqual(trigger.observe(True, True)["presses"], 1)
        self.assertEqual(trigger.observe(True, True)["presses"], 1)
        self.assertIsNone(trigger.observe(False)["pressed"])
        self.assertEqual(trigger.observe(True, True)["presses"], 1)
        self.assertEqual(trigger.observe(True, False)["releases"], 2)

    def test_default_and_reference_round_trip(self):
        config = copy.deepcopy(server.DEFAULTS)
        config['head']['reference'] = {'quaternion': [0, 0, 0, 1], 'source': 'manual', 'serial': ''}
        self.assertEqual(server.validate_config(json.loads(json.dumps(config))), config)

    def test_reject_bad_imports(self):
        mutations = [lambda c: c['screen'].update(width=0), lambda c: c['head']['mount']['position'].__setitem__(0, float('nan')),
                     lambda c: c.update(extra=True), lambda c: c['head'].update(reference={'quaternion': [0,0,0,0], 'source':'live', 'serial':'x'}),
                     lambda c: c['view'].update(staleMs=3000), lambda c: c['view'].update(near=10,far=2),
                     lambda c: (c['head'].update(serial='same'),c['weapon'].update(serial='same')),
                     lambda c: c.update(coordinateSystem='Unity')]
        for mutate in mutations:
            config=copy.deepcopy(server.DEFAULTS);mutate(config)
            with self.assertRaises(ValueError):server.validate_config(config)

    def test_participant_ids_are_whole_numbers_only(self):
        corpus = json.loads((ROOT / "tests/participant-ids.json").read_text(encoding="utf-8"))
        for case in corpus["inputs"]:
            with self.subTest(value=case["text"]):
                if case["id"] is None:
                    with self.assertRaises(ValueError):
                        server.participant_id(case["text"])
                else:
                    self.assertEqual(server.participant_id(case["text"]), case["id"])
        with tempfile.TemporaryDirectory() as scratch:
            for case in corpus["records"]:
                record = {"format": server.PARTICIPANT_FORMAT, "savedUtc": "", "calibration": server.DEFAULTS, **case["fields"]}
                path = Path(scratch) / f"participant-{case['fileId']}.json"
                path.write_text(json.dumps(record), encoding="utf-8")
                if case["valid"]:
                    loaded = server.read_participant(path)
                    self.assertEqual(loaded["participantId"], case["fileId"])
                    self.assertNotIn("name", loaded)
                else:
                    with self.assertRaises(ValueError):
                        server.read_participant(path)
        self.assertEqual(server.participant_path(0).name, 'participant-0.json')
        self.assertEqual(server.participant_path(42).name, 'participant-42.json')
        for value in ['../runtime', 'C:\\file', '', 'x/y', '3.json', 'P-3', '1e3', '-1', 1000000, 3.0, True, None]:
            with self.assertRaises(ValueError):server.participant_path(value)

    def test_native_pose_checks(self):
        matrix=[[1,0,0,1],[0,1,0,2],[0,0,1,3]]
        self.assertEqual(checked_matrix(matrix),matrix)
        for invalid in [[[1,0,0,0],[0,1,0,0],[0,0,-1,0]], [[0,0,0,0]]*3, [[1,0,0,float('inf')],[0,1,0,0],[0,0,1,0]]]:
            with self.assertRaises(ValueError):checked_matrix(invalid)

    def test_stalled_native_poll_invalidates_pose(self):
        bridge=TrackingBridge()
        try:
            bridge._publish('connected','test',[{'valid':True,'matrix':[[1,0,0,0],[0,1,0,0],[0,0,1,0]],
                                                'trigger': {'available': True, 'pressed': True}},
                                               {'valid': True, 'trigger': None}])
            bridge.sample_time=time.monotonic()-1
            value=bridge.snapshot()['devices'][0]
            self.assertFalse(value['valid']);self.assertIsNone(value['matrix'])
            self.assertFalse(value['trigger']['available']);self.assertIsNone(value['trigger']['pressed'])
        finally:bridge.close()


class ApiTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(server.os.environ, {"ELTS_STATION_HOME": ""})
        environment.start(); self.addCleanup(environment.stop)
        self.directory=tempfile.TemporaryDirectory()
        self.root_patch=patch.object(server,'ROOT',Path(self.directory.name));self.root_patch.start()
        self.instance=server.Server(('127.0.0.1',0))
        self.thread=threading.Thread(target=self.instance.serve_forever,daemon=True);self.thread.start()
        self.base=f'http://127.0.0.1:{self.instance.server_port}'

    def tearDown(self):
        self.instance.shutdown();self.instance.bridge.close();self.instance.server_close();self.thread.join()
        self.root_patch.stop();self.directory.cleanup()

    def request(self,path,body=None,token=True,origin=None,generation=None):
        headers={}
        if token:headers['X-Calibration-Token']=self.instance.token
        if origin:headers['Origin']=origin
        if generation is not None:headers['X-Dataset-Generation']=generation
        if body is not None:headers['Content-Type']='application/json'
        with urlopen(Request(self.base+path,data=json.dumps(body).encode() if body is not None else None,headers=headers)) as response:
            return json.load(response)

    def test_participant_persistence_and_inventory(self):
        config=copy.deepcopy(server.DEFAULTS);config['screen']['width']=1.7
        self.request('/api/participant',{'participantId':12,'name':'Test alias','config':config})
        self.request('/api/participant',{'participantId':3,'config':server.DEFAULTS})
        self.request('/api/participant',{'participantId':0,'name':'Zero','config':server.DEFAULTS})
        saved=self.request('/api/participant?id=12')
        self.assertEqual(saved['calibration'],config)
        self.assertEqual(saved['participantId'],12)
        self.assertNotIn('name',saved)
        inventory=self.request('/api/state')['participants']
        self.assertEqual([p['id'] for p in inventory],[0,3,12])
        self.assertTrue(all('name' not in p for p in inventory))
        record=json.loads((Path(self.directory.name)/'profiles/participant-12.json').read_text(encoding='utf-8'))
        self.assertEqual(record['format'],server.PARTICIPANT_FORMAT)
        self.assertNotIn('name',record)
        # Older files remain readable without exposing their obsolete field.
        record['name'] = 'Former test alias'
        server.write_atomic(server.participant_path(12),record)
        self.assertNotIn('name',self.request('/api/participant?id=12'))
        self.assertFalse(list(Path(self.directory.name).rglob('*.tmp')))

    def test_participant_save_preserves_the_shared_room_until_explicitly_changed(self):
        one=copy.deepcopy(server.DEFAULTS); one['screen']['width']=1.7
        two=copy.deepcopy(server.DEFAULTS); two['screen']['width']=2
        self.request('/api/participant',{'participantId':1,'config':one})
        room=Path(self.directory.name)/'station-room.json'
        self.request('/api/participant',{'participantId':2,'config':two})
        saved=json.loads(room.read_text(encoding='utf-8'))
        self.assertEqual(saved['screen']['width'],1.7)
        self.assertEqual(set(saved),{'version','coordinateSystem','screen','head','weapon'})
        self.request('/api/room',two)
        self.assertEqual(json.loads(room.read_text(encoding='utf-8'))['screen']['width'],2)

    def test_non_numeric_participant_id_is_rejected(self):
        for participant in ['P-3','abc',-2,'1.5']:
            with self.assertRaises(HTTPError) as error:
                self.request('/api/participant',{'participantId':participant,'config':server.DEFAULTS})
            self.assertEqual(error.exception.code,400)
        self.assertFalse((Path(self.directory.name)/'profiles').exists())

    def test_reset_invalidates_profile_and_draft_writes_from_an_old_tab(self):
        key = "1234567890abcdef" * 2
        draft = {"id": key, "participantId": "12", "config": server.DEFAULTS, "name": "old fixture field"}
        self.request("/api/draft", draft)
        self.assertNotIn("name", self.request("/api/draft?id=" + key))
        self.assertEqual(self.request("/api/dataset")["generation"], "")
        marker = server.draft_path(key).parent / ".dataset-generation"
        marker.write_text("new-generation", encoding="ascii")
        server.draft_path(key).unlink()
        self.assertIsNone(self.request("/api/draft?id=" + key))
        for path, record in (("/api/participant", {"participantId":12, "config":server.DEFAULTS}),
                             ("/api/draft", draft)):
            with self.assertRaises(HTTPError):
                self.request(path, record, generation="")
            self.assertFalse(server.participant_path(12).exists())
            self.assertFalse(server.draft_path(key).exists())
        self.request("/api/draft", draft, generation="new-generation")
        self.assertEqual(self.request("/api/draft?id=" + key)["participantId"], "12")

    def test_renamed_participant_file_is_not_trusted(self):
        self.request('/api/participant',{'participantId':4,'config':server.DEFAULTS})
        profiles=Path(self.directory.name)/'profiles'
        (profiles/'participant-4.json').replace(profiles/'participant-5.json')
        self.assertEqual(self.request('/api/state')['participants'],[{"id":5,"savedUtc":"","available":False}])
        with self.assertRaises(HTTPError):self.request('/api/participant?id=5')

    def test_unreadable_id_is_reserved_and_preserved_on_direct_save(self):
        profiles = Path(self.directory.name) / "profiles"
        profiles.mkdir()
        bad = profiles / "participant-0.json"
        bad.write_bytes(b"{incomplete")
        self.assertEqual(self.request("/api/state")["participants"], [{"id":0,"available":False,"savedUtc":""}])
        with self.assertRaises(HTTPError) as failure:
            self.request("/api/participant", {"participantId": 0, "config": server.DEFAULTS})
        self.assertEqual(failure.exception.code, 400)
        self.assertEqual(bad.read_bytes(), b"{incomplete")
        self.assertEqual(self.request("/api/participant", {"participantId":1, "config":server.DEFAULTS})["saved"], 1)
        self.assertEqual(bad.read_bytes(), b"{incomplete")

    def test_reject_unauthorized_writes_and_foreign_origin(self):
        for kwargs in [{'token':False},{'origin':'https://example.com'}]:
            with self.assertRaises(HTTPError) as error:self.request('/api/participant',{'participantId':1,'config':server.DEFAULTS},**kwargs)
            self.assertEqual(error.exception.code,403)

    def test_invalid_import_never_replaces_existing_profile(self):
        self.request('/api/participant',{'participantId':1,'config':server.DEFAULTS})
        invalid=copy.deepcopy(server.DEFAULTS);invalid['screen']['height']=-1
        with self.assertRaises(HTTPError):self.request('/api/participant',{'participantId':1,'config':invalid})
        self.assertEqual(self.request('/api/participant?id=1')['calibration'],server.DEFAULTS)

    def test_tracking_off_is_explicit(self):
        state=self.request('/api/tracking')
        self.assertEqual(state['status'],'off');self.assertEqual(state['devices'],[])
        self.request('/api/tracking',{'enabled':False})
        self.assertEqual(self.request('/api/tracking')['status'],'off')


if __name__=='__main__':unittest.main()
