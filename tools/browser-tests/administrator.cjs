/* Browser interaction checks against the real dashboard assets and a synthetic
 * HTTP fixture. Unity integration is covered separately by Play Mode tests. */
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const assets = path.join(root, 'unity/Assets/StreamingAssets/administrator');
const output = path.join(root, 'test-results');
fs.mkdirSync(output, { recursive: true });
const conditions = ['WE_FT', 'WE_MT', 'NE_FT', 'NE_MT'];
const commands = [];
let rejectFolder = false;
let current = {
  state: 'Idle', participant: '', runId: '', blockId: '', condition: '', canStartParticipant: true,
  conditionOrder: conditions.slice(), testDurations: Object.fromEntries(conditions.map(c => [c, 300])),
  editableDurations: conditions.slice(), blocks: [], remainingSeconds: 0, activeSeconds: 0,
  tracking: {}, camera: { yaw: 145, pitch: 20, distance: 4 },
  data: {ready:true, busy:false, path:'collection data', message:'Local collection ready', profiles:[{id:'profile1',code:'P-001',name:'Fictional participant',sessions:'2',modifiedUtc:'2026-09-11T12:00:00Z'}]},
  tools: { practiceSeconds: 10, breakSeconds: 5, saveStatus: 'No recording yet.', notes: [], checkpoints: [],
    schedule: conditions.slice(), queueIndex: 0, repeatable: [], readiness: [], calibration: {}, recordings: [] }
};
const server = http.createServer(async (req, res) => {
  if (req.url.split('?')[0] === '/api/state') { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(current)); return; }
  if (req.url === '/api/command') {
    let data = ''; for await (const part of req) data += part;
    const command = JSON.parse(data); commands.push(command);
    if(command.action === "openDataFolder" && rejectFolder){res.setHeader("Content-Type","application/json");res.end(JSON.stringify({ok:false,message:"Explorer could not open the folder."}));return;}
    if (command.action === 'applySetup') {
      current.conditionOrder = command.conditionOrder; current.testDurations = command.durations;
      current.tools.breakSeconds = command.breakSeconds; current.tools.practiceSeconds = command.practiceSeconds;
    }
    if (command.action === 'setPhaseDurations') {
      current.tools.breakSeconds = command.breakSeconds; current.tools.practiceSeconds = command.practiceSeconds;
    }
    if (command.action === 'setDuration') current.testDurations[command.condition] = command.seconds;
    if (command.action === 'startBreak') { current.state = 'Break'; current.remainingSeconds = 22; current.tools.canStartBreak = false; }
    if (command.action === 'skipBreak') { current.state = 'BlockReady'; current.tools.canSkipBreak = false; }
    if (command.action === 'viewParticipant') current.data.profile = {participant:current.data.profiles[0],sessions:[{id:'session1',modifiedUtc:'2026-09-11T12:00:00Z',finalized:'1',review:JSON.stringify({attempts:[{condition:'WE_FT',blockId:'WE_FT-attempt-01',status:'Completed',hits:2,shots:3,activeSeconds:300}],notes:[{text:'<script>must remain text</script>'}]})}]};
    if (command.action === 'viewDataRows') current.data.rows=[{offset:'0',json:JSON.stringify({eventType:'OperatorNote',payload:{text:'<img src=x onerror=alert(1)>'}})}];
    if (command.action === 'exportParticipant' || command.action === 'exportDatabase') current.downloadId='fixture-export';
    if (command.action === 'listRecordings') current.tools.recordings = [{ id: 'synthetic-browser-test', finalized: true }];
    if (command.action === 'reviewRecording') current.tools.review = { id: command.id, finalized: true,
      attempts: [{ blockId: 'WE_FT-attempt-01', condition: 'WE_FT', status: 'Completed', hits: 2, shots: 3, activeSeconds: 300, scoreStatus: 'unavailable' }],
      notes: [{ text: '<script>must remain text</script>' }], limitation: 'Synthetic browser fixture.' };
    res.setHeader('Content-Type', 'application/json'); res.end('{"ok":true}'); return;
  }
  if(req.url.startsWith('/api/download/')){res.setHeader('Content-Disposition','attachment; filename="fixture.sqlite"');res.end('SQL fixture bytes');return;}
  const name = req.url === '/' ? 'index.html' : req.url.slice(1);
  if (!['index.html', 'app.js', 'workflows.js', 'data-viewer.js', 'styles.css'].includes(name)) { res.writeHead(404); res.end(); return; }
  res.setHeader('Content-Type', name.endsWith('.js') ? 'text/javascript' : name.endsWith('.css') ? 'text/css' : 'text/html');
  res.end(fs.readFileSync(path.join(assets, name)));
});

(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({ channel: process.env.ELTS_TEST_BROWSER || 'chrome', headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, acceptDownloads: true });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const wait = async predicate => {
    for (let i = 0; i < 100; i++) { if (predicate()) return; await page.waitForTimeout(50); }
    throw new Error('Fixture condition timed out');
  };
  try {
    await page.goto(`http://127.0.0.1:${server.address().port}/#fixture`);
    await page.waitForFunction(() => document.getElementById('connectionText').textContent === 'Connected');
    await page.locator('#setupWelcome').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#startDialog').isVisible(), false, 'Participant entry must not block setup');
    // Use a loaded image: the placeholder alone cannot reproduce native image dragging.
    await page.evaluate(async () => {
      const image = document.getElementById('viewImage');
      image.src = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1200"><rect width="1920" height="1200" fill="gray"/></svg>');
      await image.decode(); image.hidden = false;
      document.getElementById('viewPlaceholder').hidden = true;
      window.previewNativeDrags = 0;
      image.addEventListener('dragstart', () => window.previewNativeDrags++);
    });
    const preview = await page.locator('#viewport').boundingBox();
    for (let drag = 0; drag < 2; drag++) {
      const beforeOrbit = commands.filter(c => c.action === 'viewOrbit').length;
      await page.mouse.move(preview.x + preview.width / 2, preview.y + preview.height / 2);
      await page.mouse.down();
      await page.mouse.move(preview.x + preview.width / 2 + 100, preview.y + preview.height / 2 + 40, { steps: 12 });
      await page.mouse.up();
      await wait(() => commands.filter(c => c.action === 'viewOrbit').length > beforeOrbit);
      assert.ok(commands.filter(c => c.action === 'viewOrbit').at(-1).yaw > 145, 'Dragging the image rotates the camera');
    }
    assert.equal(await page.evaluate(() => window.previewNativeDrags), 0, 'Orbit gestures never start native image dragging');
    assert.equal(await page.evaluate(() => window.getSelection().toString()), '', 'Orbit gestures do not select overlay text');
    await page.locator('#settingsButton').click();
    const handles = page.locator('.grab-handle');
    await handles.first().waitFor({ state: 'visible' });
    assert.equal(await page.locator('#conditionOrder [data-move]').count(), 0, 'Grab handles replace visible move arrows');
    await page.waitForTimeout(250);
    const first = await handles.nth(0).boundingBox(), third = await handles.nth(2).boundingBox();
    await page.mouse.move(first.x + 15, first.y + 15); await page.mouse.down();
    await page.mouse.move(third.x + 15, third.y + 30, { steps: 18 });
    assert.equal(await page.locator('.order-placeholder').count(), 1, 'Drag leaves a visible landing slot');
    await page.screenshot({ path: path.join(output, 'administrator-drag.png') });
    await page.mouse.up();
    await wait(() => commands.some(c => c.action === 'applySetup'));
    assert.notDeepEqual(current.conditionOrder, conditions, 'Pointer drag commits the new order');
    await page.waitForTimeout(350);
    const saved = current.conditionOrder.slice();
    const beforeCancel = commands.length;
    const handle = await handles.first().boundingBox();
    await page.mouse.move(handle.x + 15, handle.y + 15); await page.mouse.down();
    await page.mouse.move(handle.x + 15, handle.y + 90, { steps: 6 });
    await page.keyboard.press('Escape'); await page.mouse.up();
    assert.deepEqual(current.conditionOrder, saved, 'Escape restores the original order');
    assert.equal(commands.length, beforeCancel, 'Canceled drag sends no command');
    assert.equal(await page.locator('#settingsDialog').isVisible(), true, 'Escape cancels drag before closing settings');
    await handles.first().focus(); await page.keyboard.press('ArrowDown');
    await wait(() => commands.length > beforeCancel);
    await page.waitForTimeout(300);
    assert.notDeepEqual(current.conditionOrder, saved, 'Keyboard handles reorder tests');
    await page.locator('#presetName').fill('Fixture preset'); await page.locator('#savePreset').click();
    assert.match(await page.locator('#presetSelect').textContent(), /Fixture preset/);
    await page.locator('#breakSeconds').fill('45'); await page.locator('#practiceSeconds').fill('20');
    await page.locator('#applyPhaseTiming').click();
    await wait(() => current.tools.breakSeconds === 45);
    await page.locator('#presetSelect').selectOption('Fixture preset'); await page.locator('#loadPreset').click();
    await wait(() => current.tools.breakSeconds === 5);
    await page.locator('#duration-WE_FT').fill('240'); await page.locator('#duration-WE_FT').press('Tab');
    await wait(() => current.testDurations.WE_FT === 240);
    await page.waitForFunction(() => document.getElementById('durationStatus-WE_FT').textContent === 'Saved');
    assert.equal(await page.locator('#durationStatus-WE_FT').textContent(), 'Saved', 'Test duration autosaves with confirmation');
    await page.screenshot({ path: path.join(output, 'administrator-setup.png'), fullPage: true });
    await page.locator('#closeSettings').click();
    Object.assign(current, { state: 'BlockEnded', participant: 'SYNTHETIC', runId: 'fixture-run', blockId: 'WE_FT-attempt-01',
      condition: 'WE_FT', canStartParticipant: false, remainingSeconds: 22 });
    Object.assign(current.tools, { canStartBreak: true, canSkipBreak: true, canRepeat: true, repeatable: ['WE_FT'],
      saveStatus: 'Saved WE_FT · fixture checkpoint', notes: [{ seconds: 32, text: '<b>Observation stays plain text</b>' }] });
    await page.locator('#breakPanel').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#breakTimer').textContent(), '00:05');
    assert.equal(await page.locator('#armButton').isDisabled(), true);
    await page.locator('#startBreak').click();
    await wait(() => current.state === 'Break');
    await page.locator('#startBreak').waitFor({ state: 'hidden' });
    assert.equal(commands.find(c => c.action === 'startBreak').blockId, 'WE_FT-attempt-01');
    assert.equal(await page.locator('#breakTimer').textContent(), '00:22');
    assert.equal(await page.locator('#noteTimeline b').count(), 0, 'Notes are escaped');
    await page.screenshot({ path: path.join(output, 'administrator-break.png'), fullPage: true });
    await page.locator('#skipBreak').click();
    await wait(() => current.state === 'BlockReady');
    assert.equal(commands.find(c => c.action === 'skipBreak').blockId, 'WE_FT-attempt-01', 'Skip break carries the displayed attempt identity');
    await page.locator('#recordingsButton').click();
    await page.locator('[data-profile-id="profile1"]').click();
    await page.locator('#dataExportParticipant').waitFor();
    assert.equal(await page.locator('#workspace').isVisible(), false);
    assert.equal(current.state, 'BlockReady', 'Data mode preserves test administration state');
    assert.equal(await page.locator('#dataProfile script').count(), 0, 'Stored notes cannot execute markup');
    await page.locator('#dataProfile summary').click();
    await page.locator('#dataLoadRows').click();
    await page.waitForFunction(() => document.getElementById('dataRawRows').textContent.includes('OperatorNote'));
    assert.equal(await page.locator('#dataRawRows img').count(),0,'Raw JSON is escaped');
    await page.locator('#dataExportParticipant').click();
    await page.locator('#dataDownload').waitFor({state:'visible'});
    const downloaded = page.waitForEvent('download'); await page.locator('#dataDownload').click();
    assert.match((await downloaded).suggestedFilename(), /sqlite$/);
    await page.locator('#dataExportAll').click(); await wait(()=>commands.some(c=>c.action==='exportDatabase'));
    await page.locator('#dataFolder').click(); await wait(()=>commands.some(c=>c.action==='openDataFolder'));
    rejectFolder = true; await page.locator('#dataFolder').click();
    await page.waitForTimeout(500);
    assert.match(await page.locator('#dataError').textContent(), /Explorer/, 'Command errors survive state polling');
    rejectFolder = false; await page.locator('#dataFolder').click();
    const csvDownload = page.waitForEvent('download'); await page.locator('#dataReviewCsv').click();
    assert.match((await csvDownload).suggestedFilename(), /review.csv$/);
    await page.locator('#dataSearch').fill('missing');
    assert.equal(await page.locator('[data-profile-id]').count(),0,'Participant search filters profiles');
    await page.locator('#dataSearch').fill('P-001');
    current.data.error='Storage is read-only. Choose a writable local collection folder.';
    await page.locator('#dataError').waitFor({state:'visible'});
    assert.match(await page.locator('#dataError').textContent(),/read-only/,'Storage errors are visible');
    current.data.error='';
    await page.locator('#dataError').waitFor({state:'hidden'});
    current.data.profile.sessions[0].review = JSON.stringify({attempts:[{condition:'WE_FT',blockId:'WE_FT-attempt-01',status:'Completed',hits:2,shots:3,activeSeconds:300}],notes:[{text:'Fictional demonstration. Participant reported comfortable posture.'}]});
    await page.waitForFunction(()=>document.getElementById('dataProfile').textContent.includes('comfortable posture'));
    await page.screenshot({path:path.join(output,'administrator-data.png'),fullPage:true});
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: path.join(output, 'administrator-data-mobile.png'), fullPage: true });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Mobile page has no horizontal overflow');
    await page.locator('#recordingsButton').click();
    assert.equal(await page.locator('#workspace').isVisible(),true,'Toggle returns to test administration');
    const restricted = await browser.newPage();
    await restricted.addInitScript(() => Object.defineProperty(window, 'localStorage', {get(){throw new DOMException('Storage blocked','SecurityError');}}));
    await restricted.goto(`http://127.0.0.1:${server.address().port}/#fixture`);
    await restricted.waitForFunction(()=>document.getElementById('connectionText').textContent === 'Connected');
    await restricted.locator('#recordingsButton').click();
    assert.equal(await restricted.locator('#dataWorkspace').isVisible(),true,'Blocked browser preferences cannot disable controls');
    await restricted.close();
    assert.deepEqual(errors, [], 'Dashboard has no browser errors');
    console.log('PASS: administrator browser interactions (drag, cancel, keyboard, presets, timing, notes, participant profiles, SQL downloads, raw records, storage errors, mobile)');
  } catch (error) {
    await page.screenshot({ path: path.join(output, 'administrator-browser-failure.png'), fullPage: true });
    console.error('Browser errors:', errors);
    console.error('Order status:', await page.locator('#orderStatus').textContent());
    throw error;
  } finally { await browser.close(); server.close(); }
})().catch(error => { console.error(error); server.close(); process.exitCode = 1; });
