/* Capture the shipped dashboard using fictional guide data.
 * No participant records or live device connections are read or created.
 * Run with Node, Playwright and Chrome available: node tools/browser-tests/capture-user-guide.cjs
 * Review the resulting PNGs before publishing documentation. */
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const assets = path.join(root, 'unity/Assets/StreamingAssets/administrator');
const output = path.join(root, 'docs/operator/images/user-guide/texas-tech');
fs.mkdirSync(output, { recursive: true });
const conditions = ['WE_FT', 'WE_MT', 'NE_FT', 'NE_MT'];
const commands = [];
// Fictional, deterministic examples exercise the current review interface. These
// values illustrate controls only; they are not measurements from the apparatus.
const demoReview = { attempts: [{
  condition: 'WE_FT', blockId: 'WE_FT-attempt-01', status: 'Completed',
  activeSeconds: 30, shots: 30, hits: 24, misses: 6,
  hitsPerSecond: 0.8, shotsPerSecond: 1, missesPerSecond: 0.2, accuracyPercent: 80,
  meanShotErrorDeg: 1.04, p95ShotErrorDeg: 3.2, meanMissOffsetMm: 24.5,
  aimErrorVarianceDeg2: 0.04, meanAimSpeedDegPerSecond: 12.4,
  aimSpeedVariabilityDegPerSecond: 8.1, aimCoveragePercent: 94.7,
  shotGeometryCount: 30, aimObservations: 600, validAimObservations: 598,
  metricsVersion: 'elts.task-metrics.v1', scoreStatus: 'unavailable',
  seconds: Array.from({ length: 30 }, (_, i) => ({
    second: i, exposureSeconds: 1, hits: i % 5 === 0 ? 0 : 1, shots: 1,
    misses: i % 5 === 0 ? 1 : 0, hitsPerSecond: i % 5 === 0 ? 0 : 1,
    shotsPerSecond: 1, missesPerSecond: i % 5 === 0 ? 1 : 0,
    meanAimErrorDeg: 0.5 + (i % 7) / 10
  })),
  shotDetails: Array.from({ length: 30 }, (_, i) => ({
    shotNumber: i + 1, activeSeconds: i + 0.5, outcome: i % 5 === 0 ? 'Miss' : 'Hit',
    angularErrorDeg: i % 5 === 0 ? 3.2 : 0.5,
    centerOffsetMm: i % 5 === 0 ? 24.5 : 4,
    edgeClearanceMm: i % 5 === 0 ? 14.5 : 0, referenceTargetId: 'demo-target-' + i
  }))
}], notes: [] };
const profileItems = [{ id: 1, available: true, screenMatches: true }];
const details = {profiles:[{id:'demo-profile',code:'1',sessions:'1',modifiedUtc:'2026-09-21T12:00:00Z'}]};
let current = {
  state: 'Idle', participant: '', runId: '', blockId: '', condition: '', canStartParticipant: true,
  profileRevision: 1,
  conditionOrder: conditions.slice(), testDurations: Object.fromEntries(conditions.map(c => [c, 300])),
  editableDurations: conditions.slice(), blocks: [], remainingSeconds: 0, activeSeconds: 0,
  tracking: {}, camera: { yaw: 145, pitch: 20, distance: 4 },
  data:{revision:1,ready:true,busy:false,path:'collection data',message:'Local collection refreshed.'},
  tools: { practiceSeconds: 10, breakSeconds: 120, saveStatus: 'No recording yet.', latestCheckpoint: null,
    schedule: conditions.slice(), queueIndex: 0, repeatable: [], readiness: [] }
};
const server = http.createServer(async (req, res) => {
  if (req.url.split('?')[0] === '/api/state') { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(current)); return; }
  if (req.url === '/api/profiles' || req.url === '/api/data') {
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify(req.url === '/api/profiles' ? {revision:1, items:profileItems} : {revision:current.data.revision, details}));
    return;
  }
  if (req.url === '/api/command') {
    let data = ''; for await (const part of req) data += part;
    const command = JSON.parse(data); commands.push(command);
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
    if (command.action === 'viewParticipant') {
      details.profile = {participant: details.profiles[0],
        sessions: [{id:'demo-session', visitId:'demo-visit', modifiedUtc:'2026-09-21T12:00:00Z',
          finalized:'1', integrityStatus:'Unchecked', review:JSON.stringify(demoReview)}]};
      current.data.revision++;
    }
    res.setHeader('Content-Type', 'application/json'); res.end('{"ok":true}'); return;
  }
  const name = req.url === '/' ? 'index.html' : req.url.slice(1);
  if (!['index.html', 'app.js', 'workflows.js', 'data-viewer.js', 'styles.css'].includes(name)) { res.writeHead(404); res.end(); return; }
  res.setHeader('Content-Type', name.endsWith('.js') ? 'text/javascript' : name.endsWith('.css') ? 'text/css' : 'text/html');
  res.end(fs.readFileSync(path.join(assets, name)));
});

(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({ channel: process.env.ELTS_TEST_BROWSER || 'chrome', headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, deviceScaleFactor: 1 });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  // State comes through the same HTTP interface as the desktop application.
  async function settle() { await page.waitForTimeout(500); }
  async function shot(name, selector, directory = output) {
    if (process.argv.includes('--data-only') && !['01-dashboard', '13-data-viewer', '14-timeline', '15-shots'].includes(name)) return;
    const target = selector ? page.locator(selector) : page;
    if (selector) await target.scrollIntoViewIfNeeded();
    await settle();
    await target.screenshot({ path: path.join(directory, name + '.png') });
  }
  try {
    await page.goto(`http://127.0.0.1:${server.address().port}/#guide-demo`);
    await page.waitForFunction(() => document.getElementById('connectionText').textContent === 'Connected');
    await shot('01-dashboard');
    await page.locator('#settingsButton').click();
    await shot('02-test-order', '#conditionOrder');
    await shot('03-timing', '.timing-settings');
    await page.locator('#closeSettings').click();
    await page.locator('#startParticipant').click();
    // The illustrated recording starts with a typed participant ID.
    await page.locator('#participantPicker').fill('1');
    await shot('04-participant', '#startForm');
    await page.locator('#cancelDialog').click();
    Object.assign(current, { state: 'BlockReady', participant: '1', runId: '1-example',
      condition: 'WE_FT', blockId: 'WE_FT-attempt-01', durationSeconds: 300, canAbort: true,
      canStartParticipant: false, recordingPath: 'collection data / 1-example' });
    current.tools.saveStatus = 'Recording active.';
    Object.assign(current, {
      state: 'Calibration',
      tutorial: {
        started: true, stage: 'HeadCalibration', step: '03', title: 'Head-tracker calibration',
        instruction: 'Stand upright. Look straight ahead at the dot.',
        primaryLabel: 'Capture direction 2 of 7', canPrimary: true,
        canRedo: true, canPause: true, canReplay: true, audioAvailable: true,
      },
    });
    await page.locator('#tutorialPanel').waitFor({ state: 'visible' });
    await shot('dashboard-walkthrough', '#tutorialPanel', path.join(root, 'docs/images/readme'));
    delete current.tutorial;
    current.state = 'BlockReady';
    current.canArm = true; current.remainingSeconds = 300;
    current.tools.canSkipTest = true;
    await settle(); await shot('07-arm', '.task-card');
    Object.assign(current, { state: 'BlockRunning', canArm: false, canPause: true, canStop: true,
      remainingSeconds: 255, activeSeconds: 45, shots: 0, hits: 0 });
    current.tools.canSkipTest = false;
    await settle(); await shot('08-running', '.task-card');
    await page.locator('#abortButton').click();
    await shot('12-abort-session', '#abortForm');
    await page.locator('#cancelAbort').click();
    Object.assign(current, { state: 'BlockPaused', canPause: false, canResume: true });
    await settle(); await shot('09-paused', '.task-card');
    Object.assign(current, { state: 'BlockEnded', canResume: false, canStop: false, remainingSeconds: 0 });
    Object.assign(current.tools, { canStartBreak: true, canSkipBreak: true, saveStatus: 'Saved WE_FT · demo checkpoint' });
    await settle(); await shot('10-start-break', '#breakPanel');
    await page.locator('#startBreak').click();
    await settle(); await shot('11-break-countdown', '#breakPanel');
    await page.locator('#recordingsButton').click();
    await page.locator('[data-profile-id="demo-profile"]').click();
    await page.locator('#dataRemoveParticipant').waitFor();
    // A tall viewport keeps the scrollable collection visible during element
    // captures, avoiding clipped panels or blank space around offscreen content.
    await page.setViewportSize({ width: 1440, height: 2600 });
    const profileBounds = await page.locator('#dataProfile').boundingBox();
    await page.setViewportSize({ width: 1440, height: Math.ceil(profileBounds.y + profileBounds.height + 28) });
    await shot('13-data-viewer', '#dataWorkspace');
    await page.setViewportSize({ width: 1440, height: 2600 });
    await page.locator('[data-detail-tab="timeline"]').click();
    await page.locator('#chartTime').fill('10');
    await shot('14-timeline', '#taskExplorer');
    await page.locator('[data-detail-tab="shots"]').click();
    await page.locator('#shotOutcome').selectOption('Miss');
    await shot('15-shots', '#taskExplorer');
    await page.locator('#recordingsButton').click();
    Object.assign(current, { state: 'Idle', participant: '', runId: '', canAbort: false, canStartParticipant: true });
    await page.setViewportSize({ width: 1440, height: 1050 });
    await page.locator('#settingsButton').click();
    await page.locator('#openDatasetRemoval').click();
    await page.locator('#datasetConfirmation').fill('CONFIRM');
    await shot('16-confirm-data-removal', '#datasetDialog');
    assert.deepEqual(errors, []);
    console.log('PASS: requested user-guide screenshots from current dashboard assets.');
  } finally { await browser.close(); server.close(); }
})().catch(error => { console.error(error); server.close(); process.exitCode = 1; });
