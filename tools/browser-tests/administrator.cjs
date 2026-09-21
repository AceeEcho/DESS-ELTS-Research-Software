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
const taskReview = {attempts:[{
  condition:'WE_FT',blockId:'WE_FT-attempt-01',status:'Completed',activeSeconds:30,shots:30,hits:24,misses:6,
  hitsPerSecond:0.8,shotsPerSecond:1,missesPerSecond:0.2,accuracyPercent:80,
  meanShotErrorDeg:1.3,p95ShotErrorDeg:3.2,meanMissOffsetMm:24.5,aimErrorVarianceDeg2:0.42,
  meanAimSpeedDegPerSecond:12.4,aimSpeedVariabilityDegPerSecond:8.1,aimCoveragePercent:94.7,
  shotGeometryCount:30,aimObservations:600,validAimObservations:598,metricsVersion:'elts.task-metrics.v1',
  seconds:Array.from({length:30},(_,i)=>({second:i,exposureSeconds:1,hits:i%5===0?0:1,shots:1,misses:i%5===0?1:0,hitsPerSecond:i%5===0?0:1,shotsPerSecond:1,missesPerSecond:i%5===0?1:0,meanAimErrorDeg:0.5+(i%7)/10})),
  shotDetails:Array.from({length:30},(_,i)=>({shotNumber:i+1,activeSeconds:i+0.5,outcome:i%5===0?'Miss':'Hit',angularErrorDeg:i%5===0?3.2:0.5,centerOffsetMm:i%5===0?24.5:4,edgeClearanceMm:i%5===0?14.5:0,referenceTargetId:'target-'+i}))
},{condition:'WE_FT',blockId:'WE_FT-repeat-02',status:'Stopped early',activeSeconds:5,shots:0,hits:0,misses:0,hitsPerSecond:0,shotsPerSecond:0,missesPerSecond:0,metricsVersion:'elts.task-metrics.v1',reason:'Fictional repeated attempt',seconds:[],shotDetails:[]}],notes:[{text:'Fictional demonstration. Participant reported comfortable posture.'}]};
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
    if (command.action === 'exportParticipant' || command.action === 'exportDatabase' || command.action === 'exportWorkbook') current.downloadId=command.action==='exportWorkbook'?'fixture-workbook':'fixture-export';
    if (command.action === 'listRecordings') current.tools.recordings = [{ id: 'synthetic-browser-test', finalized: true }];
    if (command.action === 'reviewRecording') current.tools.review = { id: command.id, finalized: true,
      attempts: [{ blockId: 'WE_FT-attempt-01', condition: 'WE_FT', status: 'Completed', hits: 2, shots: 3, activeSeconds: 300, scoreStatus: 'unavailable' }],
      notes: [{ text: '<script>must remain text</script>' }], limitation: 'Synthetic browser fixture.' };
    res.setHeader('Content-Type', 'application/json'); res.end('{"ok":true}'); return;
  }
  if(req.url.startsWith('/api/download/')){res.setHeader('Content-Disposition','attachment; filename="'+(req.url.includes('workbook')?'fixture.xlsx':'fixture.sqlite')+'"');res.end('fixture bytes');return;}
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
    current.data.profile.sessions[0].review = JSON.stringify(taskReview);
    await page.waitForFunction(()=>document.querySelectorAll('[data-task-id]').length===2);
    // A real press can span several live polls. Keep the actual node alive until
    // pointer-up; a fast locator.click() alone misses this replacement regression.
    const stableTab = await page.locator('[data-detail-tab="timeline"]').elementHandle();
    await page.waitForTimeout(350);
    assert.equal(await stableTab.evaluate(el => el.isConnected), true, 'Live polling must not replace unchanged task buttons');
    const heldClick = async selector => {
      const control = page.locator(selector);
      await control.scrollIntoViewIfNeeded();
      const node = await control.elementHandle(), bounds = await control.boundingBox();
      await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
      await page.mouse.down(); await page.waitForTimeout(300);
      assert.equal(await node.evaluate(el => el.isConnected), true, 'Pressed control survives live polls');
      await page.mouse.up();
    };
    for (const tab of ['timeline','shots','guide','overview']) {
      await heldClick(`[data-detail-tab="${tab}"]`);
      assert.equal(await page.locator(`[data-detail-tab="${tab}"]`).getAttribute('aria-pressed'), 'true', 'Held tab press registers');
      assert.equal(await page.locator(`[data-detail-tab="${tab}"]`).evaluate(el => el === document.activeElement), true, 'Tab focus survives selection');
    }
    await heldClick('[data-task-id="WE_FT-repeat-02"]');
    assert.equal(await page.locator('[data-task-id="WE_FT-repeat-02"]').getAttribute('aria-pressed'), 'true');
    await heldClick('[data-task-id="WE_FT-attempt-01"]');
    // Observe both work and identity: unchanged polls must neither reparse a large
    // saved review nor mutate the task DOM. Mutating the cursor is tested below.
    await page.evaluate(() => {
      window.taskMutations = 0; window.reviewParses = 0;
      window.taskObserver = new MutationObserver(records => window.taskMutations += records.length);
      window.taskObserver.observe(document.getElementById('taskExplorer'), {childList:true, subtree:true, attributes:true, characterData:true});
      window.originalJsonParse = JSON.parse;
      JSON.parse = function(value, ...args) { if (typeof value === 'string' && value.includes('"attempts"')) window.reviewParses++; return window.originalJsonParse(value, ...args); };
    });
    await page.waitForTimeout(450);
    assert.deepEqual(await page.evaluate(() => {window.taskObserver.disconnect(); JSON.parse = window.originalJsonParse; return [window.taskMutations,window.reviewParses];}), [0,0], 'Unchanged polls do no task DOM work or review parsing');
    assert.equal(await page.locator('[data-chart-count="misses"]').textContent(), '1');
    const hoverTime = async time => {
      await page.locator('#ratePlot').scrollIntoViewIfNeeded();
      const box = await page.locator('#ratePlot').boundingBox();
      await page.mouse.move(box.x + box.width * (40 + time / 30 * 720) / 800, box.y + box.height / 2);
    };
    await hoverTime(5.5);
    assert.equal(await page.locator('#chartTime').inputValue(), '5');
    assert.match(await page.locator('#chartInterval').textContent(), /5–6 active s/);
    assert.equal(await page.locator('[data-chart-count="misses"]').textContent(), '1', 'Hover shows counts for the actual interval');
    const plotNode = await page.locator('#ratePlot').elementHandle();
    await page.mouse.down(); await hoverTime(6.5); await page.waitForTimeout(300); await page.mouse.up();
    assert.equal(await plotNode.evaluate(el => el.isConnected), true, 'Dragging never rebuilds the plot');
    assert.equal(await page.locator('#chartTime').inputValue(), '6');
    assert.equal(await page.locator('[data-chart-count="misses"]').textContent(), '0', 'Measured zero misses remains zero');
    await page.keyboard.down('Shift'); await page.mouse.wheel(0,100); await page.keyboard.up('Shift');
    await page.waitForFunction(() => document.getElementById('chartTime').value === '7');
    await page.locator('#chartTime').focus(); await page.keyboard.press('End');
    assert.equal(await page.locator('#chartTime').inputValue(), '29');
    await page.waitForTimeout(300);
    assert.equal(await page.locator('#chartTime').evaluate(el => el === document.activeElement), true, 'Slider retains keyboard focus through polls');
    await page.keyboard.press('ArrowLeft');
    assert.equal(await page.locator('#chartTime').inputValue(), '28');
    assert.match(await page.locator('#chartTime').getAttribute('aria-valuetext'), /28–29 active s.*misses: 0/);
    await page.locator('[data-detail-tab="timeline"]').click();
    assert.equal(await page.locator('#chartTime').inputValue(), '28', 'Graph inspection position survives tab changes within an attempt');
    await page.locator('[data-detail-tab="overview"]').click();
    assert.match(await page.locator('#taskExplorer').innerText(), /0.8/);
    await page.locator('[data-detail-tab="timeline"]').click();
    assert.equal(await page.locator('#taskExplorer tbody tr').count(),25,'Timeline paginates exact second bins');
    await page.locator('#detailNext').click();
    assert.equal(await page.locator('#taskExplorer tbody tr').count(),5);
    await page.locator('[data-detail-tab="shots"]').click();
    await page.locator('#shotOutcome').selectOption('Miss');
    assert.equal(await page.locator('#taskExplorer tbody tr').count(),6,'Shot filter isolates misses');
    await page.locator('[data-task-id="WE_FT-repeat-02"]').click();
    await page.locator('[data-detail-tab="overview"]').click();
    assert.match(await page.locator('#taskExplorer').innerText(),/Fictional repeated attempt/,'Repeated attempts stay separate');
    assert.match(await page.locator('.metric-card').first().innerText(),/0 hit/,'Measured zero is visible');
    assert.match(await page.locator('#taskExplorer').innerText(),/—/,'Missing geometry is not zero');
    await page.locator('[data-task-id="WE_FT-attempt-01"]').click();
    await page.locator('#dataExportWorkbook').click();
    await wait(()=>commands.some(c=>c.action==='exportWorkbook'));
    const workbookDownload = page.waitForEvent('download'); await page.locator('#dataDownload').click();
    assert.match((await workbookDownload).suggestedFilename(),/xlsx$/);
    await page.locator('#dataSearch').fill('missing');
    assert.equal(await page.locator('[data-profile-id]').count(),0,'Participant search filters profiles');
    await page.locator('#dataSearch').fill('P-001');
    current.data.error='Storage is read-only. Choose a writable local collection folder.';
    await page.locator('#dataError').waitFor({state:'visible'});
    assert.match(await page.locator('#dataError').textContent(),/read-only/,'Storage errors are visible');
    current.data.error='';
    await page.locator('#dataError').waitFor({state:'hidden'});
    current.data.profile.sessions[0].review = JSON.stringify(taskReview);
    await page.waitForFunction(()=>document.getElementById('dataProfile').textContent.includes('comfortable posture'));
    await page.screenshot({path:path.join(output,'administrator-data.png'),fullPage:true});
    await page.locator('.task-detail-heading').evaluate(el => el.scrollIntoView({block:'start'}));
    await page.screenshot({path:path.join(output,'administrator-task-metrics.png'),fullPage:true});
    await hoverTime(10.5);
    await page.locator('.rate-chart').screenshot({path:path.join(output,'administrator-graph-inspector.png')});
    const fractionalReview = JSON.parse(JSON.stringify(taskReview));
    fractionalReview.attempts[0].seconds = [{second:0,exposureSeconds:.25,hits:2,shots:2,misses:null,hitsPerSecond:8,shotsPerSecond:8,missesPerSecond:null,meanAimErrorDeg:null}];
    current.data.profile.sessions[0].review = JSON.stringify(fractionalReview);
    await page.waitForFunction(() => document.getElementById('chartTime').max === '0');
    assert.match(await page.locator('#chartInterval').textContent(), /0–0.25 active s/);
    assert.equal(await page.locator('[data-chart-count="shots"]').textContent(), '2', 'Partial-bin count is not confused with its rate');
    assert.equal(await page.locator('[data-chart-rate="shots"]').textContent(), '8 /s');
    assert.equal(await page.locator('[data-chart-count="misses"]').textContent(), '—', 'Unavailable counts do not become zero');
    assert.equal(await page.locator('[data-chart-dot="missesPerSecond"]').isVisible(), false);
    const longReview = JSON.parse(JSON.stringify(taskReview));
    Object.assign(longReview.attempts[0], {activeSeconds:3600,shots:18000,hits:14400,misses:3600,shotsPerSecond:5,hitsPerSecond:4,missesPerSecond:1,
      seconds:Array.from({length:3600},(_,i)=>({second:i,exposureSeconds:1,hits:4,shots:5,misses:1,hitsPerSecond:4,shotsPerSecond:5,missesPerSecond:1})),
      shotDetails:Array.from({length:18000},(_,i)=>({...taskReview.attempts[0].shotDetails[i%30],shotNumber:i+1,activeSeconds:i/5}))});
    current.data.profile.sessions[0].review = JSON.stringify(longReview);
    await page.waitForFunction(() => document.getElementById('chartTime').max === '3599');
    await page.locator('#chartTime').focus(); await page.keyboard.press('End');
    assert.match(await page.locator('#chartInterval').textContent(), /3,599–3,600 active s/);
    await heldClick('[data-detail-tab="shots"]');
    assert.equal(await page.locator('[data-detail-tab="shots"]').getAttribute('aria-pressed'), 'true', 'Large saved review remains navigable across polls');
    assert.equal(await page.locator('#taskExplorer tbody tr').count(),25,'Long-session shot table stays paginated');
    await heldClick('[data-detail-tab="overview"]');
    current.data.profile.sessions[0].review = JSON.stringify(taskReview);
    await page.waitForFunction(() => document.getElementById('chartTime').max === '29');
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
    console.log('PASS: administrator browser interactions, stable held clicks/live polling, cached large reviews, graph inspection, partial/missing values and mobile layout');
  } catch (error) {
    await page.screenshot({ path: path.join(output, 'administrator-browser-failure.png'), fullPage: true });
    console.error('Browser errors:', errors);
    console.error('Order status:', await page.locator('#orderStatus').textContent());
    throw error;
  } finally { await browser.close(); server.close(); }
})().catch(error => { console.error(error); server.close(); process.exitCode = 1; });
