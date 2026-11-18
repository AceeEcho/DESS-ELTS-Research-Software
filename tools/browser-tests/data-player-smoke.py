"""Exercise a copied Windows player through its real loopback data API.

All participants are fictional. Copies and recordings stay in an isolated temp
folder; the repository's collection is never used by this test.
The default uses manual mode for unattended data checks. With --guided the player needs participant input.
These real input checks cannot be bypassed through the administrator API. Audio
is disabled for the entire run.
"""
import argparse
from contextlib import closing
import hashlib
import json
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import threading
import time
import urllib.request
from uuid import uuid4
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--measure', action='store_true', help='Collect real station frame and command timings during the smoke')
    parser.add_argument('--guided', action='store_true', help='Exercise the narrated path with an operator supplying participant input')
    args = parser.parse_args()
    temporary = Path(tempfile.mkdtemp(prefix='ELTS SQL copied player é ')).resolve()
    assert temporary.parent == Path(tempfile.gettempdir()).resolve()
    process = None
    calibration = None
    output = ROOT / 'test-results/data-player-smoke'
    output.mkdir(parents=True, exist_ok=True)
    assertions = []
    command_timings = []
    preview_visible = True
    profile_cache = {}
    detail_cache = {}
    last_command = 0.0
    try:
        player = temporary / 'standalone player'
        shutil.copytree(args.build.resolve(), player)
        # Both real APIs must use the new temporary station, including on restart.
        station = temporary / 'station home'
        os.environ['ELTS_STATION_HOME'] = str(station)
        sys.path.insert(0, str(ROOT / 'Calibration Environment'))
        import server as calibration_app
        profiles = station / 'Calibration Environment/profiles'
        assert calibration_app.profiles_directory().resolve() == profiles
        calibration = calibration_app.Server(('127.0.0.1', 0))
        threading.Thread(target=calibration.serve_forever, daemon=True).start()
        calibration_origin = f'http://127.0.0.1:{calibration.server_port}'

        def calibration_api(endpoint, payload=None, generation=''):
            headers = {'Content-Type': 'application/json', 'X-Calibration-Token': calibration.token,
                       'Origin': calibration_origin, 'X-Dataset-Generation': generation}
            body = None if payload is None else json.dumps(payload).encode()
            request = urllib.request.Request(calibration_origin + '/api/' + endpoint, data=body, headers=headers)
            with urllib.request.urlopen(request, timeout=10) as response:
                return json.load(response)

        example = calibration_api('state')['defaults']
        for number in (901, 902):
            payload = {'participantId': number, 'config': example, 'name': "Fictional Zoë '雪'"}
            assert calibration_api('participant', payload)['saved'] == number
            assert 'name' not in json.loads((profiles / f'participant-{number}.json').read_text(encoding='utf-8'))
        draft_id = uuid4().hex
        assert calibration_api('draft', {'id': draft_id, 'participantId': 902, 'config': example})['ok']
        assertions.append('Calibration API saves both fictional numeric profiles and a draft in the temporary station')
        log = output / 'player.log'
        log.write_text('',encoding='utf-8')
        (output / 'result.json').write_text('{"result":"running"}\n',encoding='utf-8')
        def start_player(log_path):
            nonlocal process
            launched_at = time.time()
            process = subprocess.Popen([str(player / 'ELTS-Synthetic.exe'), '-noaudio',
                                        '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
                                        *(['-eltsMeasureStation'] if args.measure else []), '-eltsTestDiscovery', '-eltsNoAdminWindow', '-logFile', str(log_path)],
                                       cwd=player, creationflags=subprocess.CREATE_NO_WINDOW)
            deadline = time.monotonic() + 60
            discovery = Path(tempfile.gettempdir()) / f"elts-administrator-{process.pid}.json"
            while time.monotonic() < deadline:
                if discovery.is_file() and discovery.stat().st_mtime >= launched_at - .01:
                    try:
                        endpoint = json.loads(discovery.read_text(encoding="utf-8-sig"))
                        match = re.fullmatch(r"(http://127\.0\.0\.1:\d+)/#([0-9a-f]+)", endpoint['url'])
                        if match and endpoint['pid'] == process.pid:
                            discovery.unlink()
                            return match.groups()
                    except (OSError, json.JSONDecodeError):
                        pass
                if process.poll() is not None:
                    raise RuntimeError('Player exited during startup; see ' + str(log_path))
                time.sleep(.25)
            raise RuntimeError('Player did not publish administrator endpoint')

        origin, token = start_player(log)

        def state():
            request = urllib.request.Request(origin + '/api/state?preview=' + ('1' if preview_visible else '0'), headers={'X-Elts-Token': token})
            with urllib.request.urlopen(request, timeout=10) as response:
                live = json.load(response)
            if 'profileRevision' in live:
                if (origin, live['profileRevision']) != profile_cache.get('key'):
                    inventory = urllib.request.Request(origin + '/api/profiles', headers={'X-Elts-Token':token})
                    with urllib.request.urlopen(inventory, timeout=10) as response:
                        batch = json.load(response)
                    profile_cache.update(key=(origin, batch['revision']), items=batch['items'])
                live['participantCalibrations'] = profile_cache['items']
            if 'revision' in live.get('data', {}):
                if (origin, live['data']['revision']) != detail_cache.get('key'):
                    details = urllib.request.Request(origin + '/api/data', headers={'X-Elts-Token':token})
                    with urllib.request.urlopen(details, timeout=10) as response:
                        snapshot = json.load(response)
                    detail_cache.update(key=(origin, snapshot['revision']), items=snapshot['details'])
                live['data'] = {**detail_cache['items'], **live['data']}
            return live

        def wait(predicate, description, timeout=45):
            deadline = time.monotonic() + timeout
            latest = None
            while time.monotonic() < deadline:
                latest = state()
                if predicate(latest):
                    if command_timings and 'observedMs' not in command_timings[-1]:
                        command_timings[-1].update(observedMs=round((time.monotonic()-last_command)*1000,2), observed=description)
                    return latest
                time.sleep(.15)
            raise AssertionError(description + ': ' + json.dumps(latest, ensure_ascii=False)[:2500])

        def command(action, expect_success=True, **values):
            nonlocal last_command
            current = state()
            body = {'action': action, 'runId': current['runId'], 'blockId': current['blockId'],
                    'tutorialRevision': current.get('tutorial', {}).get('revision', -1), **values}
            request = urllib.request.Request(origin + '/api/command', data=json.dumps(body).encode(),
                                             headers={'Content-Type': 'application/json', 'X-Elts-Token': token, 'Origin': origin})
            started = last_command = time.monotonic()
            with urllib.request.urlopen(request, timeout=10) as response:
                result = json.load(response)
            command_timings.append({'action':action, 'ackMs':round((time.monotonic() - started) * 1000, 2)})
            assert bool(result.get('ok')) == expect_success, (action, result)
            # Commands are accepted before background work finishes; wait until a
            # fresh state snapshot arrives before checking its completion flag.
            time.sleep(.25)
            return result

        def data_idle():
            result = wait(lambda s: not s['data']['busy'], 'Data operation completion')
            assert not result['data']['error'], result['data']['error']
            return result

        def prepare_walkthrough():
            command('tutorialBegin')
            deadline = time.monotonic() + 240
            announced = set()
            while time.monotonic() < deadline:
                current = state()
                if current['canArm']:
                    return
                tutorial = current['tutorial']
                stage = tutorial['stage']
                if current['busy']:
                    time.sleep(.15)
                    continue
                if stage in ('Fire', 'Reload') and not tutorial['canPrimary'] and stage not in announced:
                    print('PARTICIPANT INPUT: ' + ('Click and release in the game.' if stage == 'Fire' else 'Press R in the game.'), flush=True)
                    announced.add(stage)
                if tutorial['canPrimary']:
                    command(tutorial['primaryAction'])
                elif stage == 'PracticeRunning':
                    command('tutorialFinishPractice')
                else:
                    time.sleep(.15)
            raise AssertionError('Walkthrough did not finish; participant click/reload required: ' + json.dumps(state()['tutorial']))

        def confirm_form():
            wait(lambda s: s['tutorial']['stage'] == 'Form' and s['tutorial']['canPrimary'], 'Final recording close and form prompt')
            command('tutorialForm')
            wait(lambda s: s['canStartParticipant'] and not s['busy'], 'Form receipt saved')

        ready = wait(lambda s: s.get('data', {}).get('ready') and not s['data']['busy'], 'Storage initialization')
        collection = Path(ready['data']['path']).resolve()
        assert collection.is_relative_to(station), collection
        assert (collection / 'collection.sqlite').is_file()
        assertions.append('A copied player creates its own collection folder and SQLite database before recording')
        conditions = ['WE_FT', 'WE_MT', 'NE_FT', 'NE_MT']
        command('applySetup', conditionOrder=conditions, durations={c: 1 for c in conditions}, practiceSeconds=0, breakSeconds=0)
        for iteration in range(2):
            preview_visible = iteration == 0
            command('startParticipant', participant='901', conditionOrder=conditions)
            wait(lambda s: s['state'] == 'Calibration' and not s['busy'], 'Calibration setup')
            if iteration == 0:
                command('startParticipant',expect_success=False,participant='901',conditionOrder=conditions)
                command('note',expect_success=False,text='Rejected fixture note')
            if args.guided:
                prepare_walkthrough()
            else:
                command('tutorialManual')
                wait(lambda s: s['state'] == 'BlockReady', 'Manual session preparation')
            reasons = json.loads((ROOT / 'tools/runtime-tests/fixtures/recording-text.json').read_text())['cases']
            if iteration == 0:
                for case in reasons:
                    if not case['accepted']:
                        command('skipTest', expect_success=False, reason=case['unit'] * case['repeat'])
                assert state()['state'] == 'BlockReady'
            accepted = [case['unit'] * case['repeat'] for case in reasons if case['accepted']]
            for index in range(4):
                reason = accepted[index] if iteration == 0 and index < len(accepted) else 'Synthetic checkpoint test'
                command('skipTest', reason=reason)
                wait(lambda s: s['state'] == 'BlockEnded' and not s['busy'], 'Raw and SQL checkpoint saved')
                command('skipBreak')
                wait(lambda s: (s['state'] == 'SessionComplete' if index == 3 else s['state'] == 'BlockReady') and not s['busy'], 'Next state')
            confirm_form()
            if iteration == 0:
                command('repeatTest', condition='WE_FT', reason='Synthetic continuation identity check')
                wait(lambda s: s['state'] == 'BlockReady' and not s['busy'], 'Continuation ready')
                command('skipTest', reason='Synthetic repeated-test checkpoint')
                wait(lambda s: s['state'] == 'BlockEnded' and not s['busy'], 'Continuation checkpoint saved')
                command('skipBreak')
                confirm_form()
        command('startParticipant', participant='902', conditionOrder=conditions)
        wait(lambda s: s['state'] == 'Calibration' and not s['busy'], 'Second profile setup')
        command('abort')
        wait(lambda s: s['canStartParticipant'] and not s['busy'], 'Abort saved')
        command('refreshData', runId='stale-run-is-safe-for-data')
        data = data_idle()['data']
        assert len(data['profiles']) == 2, data
        profile = next(p for p in data['profiles'] if p['code'] == '901')
        assert profile['sessions'] == '3'
        assertions.append('Repeated sessions share one profile; a different participant has a separate profile')
        with closing(sqlite3.connect(collection / 'collection.sqlite')) as db:
            assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
            assert not db.execute('PRAGMA foreign_key_check').fetchall()
            assert not db.execute("SELECT name FROM sqlite_master WHERE name IN ('records','stream_progress')").fetchall()
            directories = [Path(row[0]) for row in db.execute('SELECT directory FROM sessions')]
            assert not db.execute("SELECT name FROM pragma_table_info('participants') WHERE name='name'").fetchall()
        for directory in directories:
            for name in ['events.ndjson', 'participant.json']:
                assert 'Fictional Zoë' not in (directory / name).read_text(encoding='utf-8'), (directory, name)
        assertions.append('SQLite holds no raw-line copy or participant name column; legacy names stay out of recordings')
        command('viewParticipant', id=profile['id'])
        selected = data_idle()['data']['profile']
        assert len(selected['sessions']) == 3
        assert sorted(len(json.loads(session['review'])['attempts']) for session in selected['sessions']) == [1,4,4]
        assert all(session['analysisEligibility'] == 'Excluded' and session['visitEligibility'] == 'Excluded'
                   for session in selected['sessions'])
        command('viewDataRows', session=selected['sessions'][0]['id'], stream='events.ndjson', page=0)
        rows = data_idle()['data']['rows']
        with closing(sqlite3.connect(collection / 'collection.sqlite')) as db:
            directory = Path(db.execute('SELECT directory FROM sessions WHERE id=?', (selected['sessions'][0]['id'],)).fetchone()[0])
        raw_lines = (directory / 'events.ndjson').read_text(encoding='utf-8').splitlines()
        assert [row['json'] for row in rows] == raw_lines[:50], 'Viewer pages read the original lines in place'
        assertions.append('The original-records viewer reads complete lines directly from the recording')
        with closing(sqlite3.connect(collection / 'collection.sqlite')) as db:
            assert db.execute('SELECT count(*) FROM task_results WHERE shots IS NOT NULL').fetchone()[0] == 0, 'Skipped attempts are not zero-score tests'
        command('exportResults')
        exported = data_idle()
        workbook = output / 'results.xlsx'
        request = urllib.request.Request(origin + '/api/download/' + exported['downloadId'] + '?token=' + token)
        with urllib.request.urlopen(request) as response:
            assert response.headers['Content-Type'] == 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
            workbook.write_bytes(response.read())
        import zipfile
        import xml.etree.ElementTree as ET
        with zipfile.ZipFile(workbook) as bundle:
            ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
            names = [node.get('name') for node in ET.fromstring(bundle.read('xl/workbook.xml')).findall('.//s:sheet', ns)]
            assert names == ['Results', 'Shots', 'Timeline', 'Metric guide', 'Recordings', 'Handoff'], names
            sheet = ET.fromstring(bundle.read('xl/worksheets/sheet1.xml'))
            text = [node.text for node in sheet.findall('.//s:t', ns)]
            assert '901' in text
            assert 'The attempt was skipped.' in text, 'Results state why the 300-second analysis excludes the skipped attempts'
            assert len(sheet.findall('.//s:row', ns)) == 10, 'Eight skipped tasks plus continuation plus header'
            assert not any('Fictional Zo' in bundle.read(name).decode('utf-8') for name in bundle.namelist())
            assert bundle.testzip() is None
        assertions.append('Results workbook covers every participant by code, without names; skipped metrics remain blank')
        command('exportSessionRaw', id=selected['sessions'][0]['id'])
        exported = data_idle()
        archive = output / 'raw.zip'
        urllib.request.urlretrieve(origin + '/api/download/' + exported['downloadId'] + '?token=' + token, archive)
        import zipfile
        with zipfile.ZipFile(archive) as bundle:
            assert 'session-summary.json' in bundle.namelist()
            assert bundle.testzip() is None
        assertions.append('Real player results workbook and raw archive downloads open independently')
        # Exercise both previously excluded originals and indexed recordings.
        command('removeRun', id=selected['sessions'][0]['id'], participant=profile['id'], confirmed=True)
        data_idle()
        with closing(sqlite3.connect(collection / 'collection.sqlite')) as db:
            assert db.execute('SELECT count(*) FROM removed_recordings').fetchone()[0] == 1
        assert all(d.resolve().is_relative_to(station) for d in directories)
        assert Path(state()['data']['path']).resolve() == collection and profiles.resolve().is_relative_to(station)
        if args.measure:
            measured = state().get('performance')
            if not measured:
                raise AssertionError('Player has no station performance instrumentation')
            from statistics import median, quantiles
            samples = [c['ackMs'] for c in command_timings]
            closed = [json.loads((d / 'session-summary.json').read_text(encoding='utf-8-sig'))['counts'] for d in directories]
            (output / 'station-performance.json').write_text(json.dumps({
                'frames':measured, 'commandToAcknowledgmentMs':{'median':median(samples), 'p95':quantiles(samples,n=100,method='inclusive')[94]},
                'commands':command_timings, 'closedRecordingCounts':closed
            },indent=2),encoding='utf-8')
        # Lose the test-owned process after a durable checkpoint. A new player
        # must recover the visit and expose a recovery ZIP with the original bytes.
        command('startParticipant', participant='902', conditionOrder=conditions)
        wait(lambda s: s['state'] == 'Calibration' and not s['busy'], 'Interruption fixture created')
        command('tutorialManual')
        wait(lambda s: s['state'] == 'BlockReady' and not s['busy'], 'Interruption fixture ready')
        command('skipTest', reason='Fictional interruption rehearsal')
        stopped = wait(lambda s: s['state'] == 'BlockEnded' and not s['busy'], 'Durable interruption checkpoint')
        interrupted = Path(stopped['recordingPath']).resolve()
        assert interrupted.parent == collection and not (interrupted / 'session-summary.json').exists()
        process.terminate()
        process.wait(timeout=10)
        origin, token = start_player(output / 'recovery-player.log')
        wait(lambda s: s.get('data', {}).get('ready') and not s['data']['busy'], 'Recovery player opened')
        with closing(sqlite3.connect(collection / 'collection.sqlite')) as db:
            session_id = db.execute('SELECT id FROM sessions WHERE directory=?', (str(interrupted),)).fetchone()[0]
        command('exportSessionRaw', id=session_id, recovery=True)
        recovered = data_idle()
        recovery_copy = output / 'recovery.zip'
        urllib.request.urlretrieve(origin + '/api/download/' + recovered['downloadId'] + '?token=' + token, recovery_copy)
        with zipfile.ZipFile(recovery_copy) as bundle:
            assert bundle.read('events.ndjson') == (interrupted / 'events.ndjson').read_bytes()
            assert 'session-summary.json' not in bundle.namelist()
        # Emulate an inaccessible source, then release it and retry the same refresh.
        from tools.data.station_copy import exclusive
        with exclusive(interrupted / 'participant.json'):
            command('refreshData', rebuild=True)
            failed = wait(lambda s: not s['data']['busy'] and bool(s['data']['error']), 'Failed index refresh')
            assert interrupted.name in failed['data']['error']
        command('refreshData')
        data_idle()
        directories.append(interrupted)
        assertions.append('A duplicate intake and retired note command are rejected. An interrupted visit reopens, exports intact, and survives a failed index refresh.')
        request_id = uuid4().hex
        command('wipeDataset', confirmation='CONFIRM', request=request_id, version=state()['datasetRemoval']['version'])
        removed = wait(lambda s: s['datasetRemoval']['request'] == request_id and
                       s['datasetRemoval']['state'] in ('complete', 'error'), 'Permanent removal')
        assert removed['datasetRemoval']['state'] == 'complete', removed['datasetRemoval']

        def check_empty():
            current = wait(lambda s: s.get('data', {}).get('ready') and not s['data']['busy'] and not s['participantCalibrations'],
                           'Empty station ready')
            assert not current['data']['profiles'] and not current['runId'] and not current['downloadId']
            assert not any(d.exists() for d in directories)
            assert not list(collection.glob('.*.reservation')) and not list((collection / 'exports').glob('*'))
            assert calibration_api('state')['participants'] == [] and calibration_api('draft?id=' + draft_id) is None
            assert (profiles.parent / 'station-room.json').is_file() and workbook.is_file() and archive.is_file()
            with closing(sqlite3.connect(collection / 'collection.sqlite')) as db:
                for table in ('participants', 'sessions', 'task_results', 'removed_recordings'):
                    assert db.execute(f'SELECT count(*) FROM {table}').fetchone()[0] == 0, table

        check_empty()
        assertions.append('Permanent removal empties originals, reservations, index exclusions, calibration profiles, drafts and managed exports while preserving room and downloaded copies')
        process.terminate()
        process.wait(timeout=10)
        origin, token = start_player(output / 'restarted-player.log')
        check_empty()
        assertions.append('The restarted copied player opens an empty dataset without reseeding participants')
        report = {'result': 'pass', 'checks': assertions, 'physicalValidation': False,
                  'playerSha256': hashlib.sha256((player / 'ELTS-Synthetic.exe').read_bytes()).hexdigest(),
                  'buildInfoSha256': hashlib.sha256((player / 'build-info.json').read_bytes()).hexdigest()}
        (output / 'result.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
        print('PASS: copied-player storage, checkpoint indexing, profile grouping, exports and permanent reset across restart')
    except Exception as error:
        (output / 'result.json').write_text(json.dumps({'result':'fail','checks':assertions,
            'error':str(error),'physicalValidation':False},indent=2)+'\n',encoding='utf-8')
        raise
    finally:
        if process is not None and process.poll() is None:
            # Only this test-owned process is terminated, after recordings close.
            process.terminate()
            process.wait(timeout=10)
        if calibration is not None:
            calibration.shutdown()
            calibration.server_close()
            calibration.bridge.close()
        assert temporary.parent == Path(tempfile.gettempdir()).resolve() and temporary.name.startswith('ELTS SQL copied player ')
        shutil.rmtree(temporary)


if __name__ == '__main__':
    main()
