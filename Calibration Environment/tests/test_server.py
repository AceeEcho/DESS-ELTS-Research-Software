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

    def test_path_traversal_rejected(self):
        for name in ['../runtime', 'C:\\file', '', 'x/y', 'x.json', 'x' * 65]:
            with self.assertRaises(ValueError):server.profile_path(name)

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
        self.directory=tempfile.TemporaryDirectory()
        self.root_patch=patch.object(server,'ROOT',Path(self.directory.name));self.root_patch.start()
        self.instance=server.Server(('127.0.0.1',0))
        self.thread=threading.Thread(target=self.instance.serve_forever,daemon=True);self.thread.start()
        self.base=f'http://127.0.0.1:{self.instance.server_port}'

    def tearDown(self):
        self.instance.shutdown();self.instance.bridge.close();self.instance.server_close();self.thread.join()
        self.root_patch.stop();self.directory.cleanup()

    def request(self,path,body=None,token=True,origin=None):
        headers={}
        if token:headers['X-Calibration-Token']=self.instance.token
        if origin:headers['Origin']=origin
        if body is not None:headers['Content-Type']='application/json'
        with urlopen(Request(self.base+path,data=json.dumps(body).encode() if body is not None else None,headers=headers)) as response:
            return json.load(response)

    def test_profile_persistence_and_inventory(self):
        config=copy.deepcopy(server.DEFAULTS);config['screen']['width']=1.7
        self.request('/api/profile',{'name':'Lab rig 1','config':config})
        self.assertEqual(self.request('/api/profile?name=Lab%20rig%201'),config)
        self.assertEqual(self.request('/api/state')['profiles'],['Lab rig 1'])
        self.assertFalse(list(Path(self.directory.name).rglob('*.tmp')))

    def test_reject_unauthorized_writes_and_foreign_origin(self):
        for kwargs in [{'token':False},{'origin':'https://example.com'}]:
            with self.assertRaises(HTTPError) as error:self.request('/api/profile',{'name':'Test','config':server.DEFAULTS},**kwargs)
            self.assertEqual(error.exception.code,403)

    def test_invalid_import_never_replaces_existing_profile(self):
        self.request('/api/profile',{'name':'Test','config':server.DEFAULTS})
        invalid=copy.deepcopy(server.DEFAULTS);invalid['screen']['height']=-1
        with self.assertRaises(HTTPError):self.request('/api/profile',{'name':'Test','config':invalid})
        self.assertEqual(self.request('/api/profile?name=Test'),server.DEFAULTS)

    def test_tracking_off_is_explicit(self):
        state=self.request('/api/tracking')
        self.assertEqual(state['status'],'off');self.assertEqual(state['devices'],[])
        self.request('/api/tracking',{'enabled':False})
        self.assertEqual(self.request('/api/tracking')['status'],'off')


if __name__=='__main__':unittest.main()
