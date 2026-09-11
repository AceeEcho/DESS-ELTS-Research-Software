"""Exercise a copied Windows player through its real loopback data API.

All participants are fictional. Copies and recordings stay in an isolated temp
folder; the repository's collection is never used by this test.
"""
import argparse
from contextlib import closing
import json
import re
import shutil
import sqlite3
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--build', type=Path, required=True)
    args = parser.parse_args()
    temporary = Path(tempfile.mkdtemp(prefix='ELTS SQL copied player é ')).resolve()
    assert temporary.parent == Path(tempfile.gettempdir()).resolve()
    process = None
    output = ROOT / 'test-results/data-player-smoke'
    output.mkdir(parents=True, exist_ok=True)
    assertions = []
    try:
        player = temporary / 'standalone player'
        shutil.copytree(args.build.resolve(), player)
        log = output / 'player.log'
        log.write_text('',encoding='utf-8')
        (output / 'result.json').write_text('{"result":"running"}\n',encoding='utf-8')
        process = subprocess.Popen([str(player / 'ELTS-Synthetic.exe'), '-batchmode',
                                    '-eltsNoAdminWindow', '-logFile', str(log)], cwd=player,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        origin = token = ''
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            text = log.read_text(encoding='utf-8', errors='replace') if log.exists() else ''
            match = re.search(r'ELTS_ADMINISTRATOR_READY (http://127\.0\.0\.1:\d+)/#([0-9a-f]+)', text)
            if match:
                origin, token = match.groups()
                break
            if process.poll() is not None:
                raise RuntimeError('Player exited during startup; see ' + str(log))
            time.sleep(.25)
        assert origin, 'Player did not publish administrator endpoint'

        def state():
            request = urllib.request.Request(origin + '/api/state', headers={'X-Elts-Token': token})
            with urllib.request.urlopen(request, timeout=10) as response:
                return json.load(response)

        def wait(predicate, description, timeout=45):
            deadline = time.monotonic() + timeout
            latest = None
            while time.monotonic() < deadline:
                latest = state()
                if predicate(latest):
                    return latest
                time.sleep(.15)
            raise AssertionError(description + ': ' + json.dumps(latest, ensure_ascii=False)[:2500])

        def command(action, **values):
            current = state()
            body = {'action': action, 'runId': current['runId'], 'blockId': current['blockId'], **values}
            request = urllib.request.Request(origin + '/api/command', data=json.dumps(body).encode(),
                                             headers={'Content-Type': 'application/json', 'X-Elts-Token': token, 'Origin': origin})
            with urllib.request.urlopen(request, timeout=10) as response:
                result = json.load(response)
            assert result.get('ok'), (action, result)
            # Commands are accepted before background work finishes; wait until a
            # fresh state snapshot arrives before checking its completion flag.
            time.sleep(.25)
            return result

        def data_idle():
            result = wait(lambda s: not s['data']['busy'], 'Data operation completion')
            assert not result['data']['error'], result['data']['error']
            return result

        ready = wait(lambda s: s.get('data', {}).get('ready') and not s['data']['busy'], 'Storage initialization')
        collection = Path(ready['data']['path'])
        assert collection.is_relative_to(player), collection
        assert (collection / 'collection.sqlite').is_file()
        assertions.append('A copied player creates its own collection folder and SQLite database before recording')
        conditions = ['WE_FT', 'WE_MT', 'NE_FT', 'NE_MT']
        command('applySetup', conditionOrder=conditions, durations={c: 1 for c in conditions}, practiceSeconds=0, breakSeconds=0)
        for iteration in range(2):
            command('startParticipant', participant='SQL-SMOKE-01', participantName="Fictional Zoë '雪'", initialNotes='Synthetic software verification only.', conditionOrder=conditions)
            wait(lambda s: s['state'] == 'Calibration' and not s['busy'], 'Calibration setup')
            command('generateCalibration')
            wait(lambda s: s['tools']['calibration']['canAccept'], 'Calibration review')
            command('acceptCalibration')
            command('startPractice')
            wait(lambda s: s['state'] == 'BlockReady' and not s['busy'], 'Ready after practice')
            for index in range(4):
                command('skipTest', reason='Synthetic software save checkpoint test')
                wait(lambda s: s['state'] == 'BlockEnded' and not s['busy'], 'Raw and SQL checkpoint saved')
                command('skipBreak')
                wait(lambda s: (s['state'] == 'SessionComplete' if index == 3 else s['state'] == 'BlockReady') and not s['busy'], 'Next state')
            wait(lambda s: s['canStartParticipant'] and not s['busy'], 'Final recording close')
            if iteration == 0:
                command('repeatTest', condition='WE_FT', reason='Synthetic continuation identity check')
                wait(lambda s: s['state'] == 'BlockReady' and not s['busy'], 'Continuation ready')
                command('skipTest', reason='Synthetic repeated-test checkpoint')
                wait(lambda s: s['state'] == 'BlockEnded' and not s['busy'], 'Continuation checkpoint saved')
                command('skipBreak')
                wait(lambda s: s['state'] == 'SessionComplete' and s['canStartParticipant'] and not s['busy'], 'Continuation finalized')
        command('startParticipant', participant='SQL-SMOKE-02', participantName='Other fictional person', conditionOrder=conditions)
        wait(lambda s: s['state'] == 'Calibration' and not s['busy'], 'Second profile setup')
        command('abort', reason='Synthetic software export isolation check')
        wait(lambda s: s['canStartParticipant'] and not s['busy'], 'Abort saved')
        command('refreshData', runId='stale-run-is-safe-for-data')
        data = data_idle()['data']
        assert len(data['profiles']) == 2, data
        profile = next(p for p in data['profiles'] if p['code'] == 'SQL-SMOKE-01')
        assert profile['sessions'] == '3'
        assertions.append('Repeated sessions share one profile; a different participant has a separate profile')
        with closing(sqlite3.connect(collection / 'collection.sqlite')) as db:
            assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
            assert not db.execute('PRAGMA foreign_key_check').fetchall()
            for session_id, directory in db.execute('SELECT id,directory FROM sessions'):
                for stream in ['events.ndjson', 'samples.ndjson', 'targets.ndjson']:
                    lines = len((Path(directory) / stream).read_text(encoding='utf-8').splitlines())
                    saved = db.execute('SELECT count(*) FROM records WHERE sessionId=? AND stream=?', (session_id, stream)).fetchone()[0]
                    assert saved == lines, (directory, stream, saved, lines)
        assertions.append('Every finalized raw event, sample and target line is also present in SQLite')
        command('viewParticipant', id=profile['id'])
        selected = data_idle()['data']['profile']
        assert len(selected['sessions']) == 3
        assert sorted(len(json.loads(session['review'])['attempts']) for session in selected['sessions']) == [1,4,4]
        command('viewDataRows', session=selected['sessions'][0]['id'], stream='events.ndjson', page=0)
        assert data_idle()['data']['rows']
        for action, expected in [('exportParticipant', 1), ('exportDatabase', 2)]:
            command(action, id=profile['id'])
            exported = data_idle()
            path = output / (action + '.sqlite')
            urllib.request.urlretrieve(origin + '/api/download/' + exported['downloadId'] + '?token=' + token, path)
            with closing(sqlite3.connect(path)) as db:
                assert db.execute('SELECT count(*) FROM participants').fetchone()[0] == expected
                assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
                assert not db.execute('PRAGMA foreign_key_check').fetchall()
        command('exportSessionRaw', id=selected['sessions'][0]['id'])
        exported = data_idle()
        archive = output / 'raw.zip'
        urllib.request.urlretrieve(origin + '/api/download/' + exported['downloadId'] + '?token=' + token, archive)
        import zipfile
        with zipfile.ZipFile(archive) as bundle:
            assert 'session-summary.json' in bundle.namelist()
            assert bundle.testzip() is None
        assertions.append('Real player SQL, participant-only SQL and original-file ZIP exports download and open independently')
        (output / 'result.json').write_text(json.dumps({'result':'pass','checks':assertions,'physicalValidation':False},indent=2)+'\n',encoding='utf-8')
        print('PASS: copied-player storage, checkpoint indexing, profile grouping, downloads and export isolation')
    finally:
        if process is not None and process.poll() is None:
            # Only this test-owned process is terminated, after recordings close.
            process.terminate()
            process.wait(timeout=10)
        assert temporary.parent == Path(tempfile.gettempdir()).resolve() and temporary.name.startswith('ELTS SQL copied player ')
        shutil.rmtree(temporary)


if __name__ == '__main__':
    main()
