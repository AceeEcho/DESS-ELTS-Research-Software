/* ELTS administrator console. The server remains the source of truth.
 * This page only renders state and sends explicit operator commands. */
(() => {
  const $ = (id) => document.getElementById(id);
  const conditions = ['WE_FT', 'WE_MT', 'NE_FT', 'NE_MT'];
  const token = decodeURIComponent(location.hash.replace(/^#/, ''));
  // Browser privacy policies can block preference storage. Controls and recording
  // still work; only the optional layout preferences become session-local.
  const preference = {
    get(key) { try { return localStorage.getItem(key); } catch { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* Optional preference. */ } }
  };
  const savedPane = Number(preference.get('elts-admin-pane'));
  let imageLoading = false;
  let intakeShown = false, submitting = false, orbitPending = false, orbitDirty = false;
  let orbitLocalUntil = 0;
  let abortRunId = "", settingsOpener = null;
  let testActionBusy = false, stopBlockId = '', stopRunId = '';
  const durationDrafts = new Set(), durationSaves = new Map(), durationFeedback = new Map();
  let state = null, order = conditions.slice(), pollBusy = false, viewVersion = null;
  let workflows, dataViewer;
  let profileCache = {revision:-1, items:[]};
  let refreshIdle = Promise.resolve();
  let orbit = { yaw: -32, pitch: 16, distance: 4.8 }, dragging = false, lastPoint = null;
  const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const command = async (action, payload = {}) => {
    const response = await fetch('/api/command', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Elts-Token': token }, body: JSON.stringify({ action, runId: state?.runId || '', blockId: state?.blockId || '', ...payload }) });
  const result = await response.json();
  if (!result.ok) throw new Error(result.message || 'Command was rejected.');
  return result;
  };
  const notifyError = (element, error) => { element.textContent = error.message || String(error);
  element.hidden = false; };
  const formatTime = (seconds) => { const total = Math.max(0, Math.floor(Number(seconds) || 0));
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`; };
  const titleCase = (value) => String(value || '').replaceAll('_', ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
  function setConnection(online) {
    $('connectionDot').classList.toggle('offline', !online);
  $('connectionText').textContent = online ? 'Connected' : 'Offline';
  }
  function renderBlocks() {
    const blocks = state?.blocks || [];
    const tools = state?.tools;
    const rows = blocks.map(block => `<tr class="${block.attempt === state.blockId ? 'current' : ''}"><td>${escapeHtml(block.condition)}<small class="attempt-label">${escapeHtml(block.attempt)}</small></td><td class="${block.status === 'Completed' ? 'done' : ''}">${escapeHtml(block.status)}</td><td>${block.status === 'Skipped' ? '—' : block.hits}</td><td>${block.status === 'Skipped' ? '—' : block.shots}</td></tr>`).join('') +
      (!state?.participant ? (state.conditionOrder || order) : state?.canStartParticipant ? [] : (tools?.schedule || order).slice(tools?.queueIndex || 0))
        .filter((condition, index) => state?.canStartParticipant || index > 0 || !blocks.some(block => block.attempt === state.blockId))
        .map(condition => `<tr><td>${escapeHtml(condition)}</td><td>Queued</td><td>—</td><td>—</td></tr>`).join('');
    if ($('blocks').dataset.rows !== rows) { $('blocks').dataset.rows = rows; $('blocks').innerHTML = rows || '<tr><td colspan="4" class="subtle">No conditions loaded</td></tr>'; }
    const planned = tools?.schedule?.length || order.length;
    const finished = blocks.filter(block => ['Completed', 'Stopped early', 'Skipped'].includes(block.status)).length;
    $('blockSummary').textContent = `${finished}/${planned} finished`;
  }
  function render() {
    if (!state) return;
    dataViewer?.render(state);
  const phase = state.phase || state.state || 'idle';
  const running = /running|armed|ready|waiting/i.test(`${phase} ${state.state || ''}`);
  $('displayCheck').disabled = !state.canStartParticipant;
  $('captions').checked = !!state.captions;
  $('currentTask').hidden = !state.participant;
  if (state.previewFps != null && document.activeElement !== $('previewRate')) $('previewRate').value = String(state.previewFps);
  $('participant').textContent = state.participant || '—';
  $('runId').textContent = state.runId ? `Run ${state.runId}` : 'No run';
  $('phaseText').textContent = titleCase(phase);
  $('condition').textContent = state.condition || 'No condition selected';
  $('remaining').textContent = state.remainingSeconds == null ? '—' : formatTime(state.remainingSeconds);
  $('remaining').hidden = !['BlockReady','BlockRunning','BlockPaused'].includes(state.state);
  if(state.state === 'BlockReady') $('remaining').textContent = formatTime(state.durationSeconds);
  $('elapsed').textContent = formatTime(state.activeSeconds);
  $('testTiming').hidden = !['BlockRunning','BlockPaused'].includes(state.state);
  $('testTiming').textContent = `Total duration · ${formatTime(state.durationSeconds || 300)}`;
  $('shots').textContent = state.shots ?? 0;
  $('hits').textContent = state.hits ?? 0;
  $('ammo').textContent = `${state.ammo ?? 20} / ${state.magazineCapacity ?? 20}`;
  for (const key of ['headHits','bodyHits','limbHits','coverHits','misses','targetKills','damageDealt','reloads','dryFires']) $(key).textContent = state[key] ?? 0;
  $('accuracy').textContent = state.shots ? `${Math.round((Number(state.hits || 0) / Number(state.shots)) * 100)}%` : '—';
  const badge = $('stateBadge');
  badge.textContent = titleCase(state.state || phase).toUpperCase();
  badge.className = `badge ${state.armed ? 'armed' : running ? 'running' : /abort|error/i.test(phase) ? 'error' : ''}`;
  const armText = state.state === 'BlockEnded' ? (state.busy ? 'Saving results…' : 'Test saved')
    : state.state === 'Break' ? 'Break in progress'
    : state.canResume ? 'Test paused'
    : state.canPause ? 'Test running'
    : state.armed ? 'Armed · waiting for first shot'
    : state.canArm ? 'Ready to arm' : state.message || (state.participant ? 'Preparing participant…' : 'Waiting for participant');
  $('armStatus').innerHTML = `<span class="status-dot ${state.armed ? 'warn' : state.canArm ? '' : 'offline'}"></span><span>${escapeHtml(armText)}</span>`;
  $('armButton').disabled = !state.canArm && !state.armed;
  $('armButton').textContent = state.armed ? 'DISARM TEST' : 'ARM TEST';
  $('armButton').classList.toggle('armed', !!state.armed);
  $('disarmButton').disabled = !state.armed;
  $('nextButton').disabled = state.canNext !== true;
  $('nextButton').hidden = true; // Break controls appear in the dedicated break panel.
  $('abortButton').disabled = state.canAbort !== true;
  $('pauseButton').disabled = testActionBusy || state.canPause !== true;
  $('pauseButton').hidden = !!state.canResume;
  $('resumeButton').disabled = testActionBusy || state.canResume !== true;
  $('resumeButton').hidden = !state.canResume;
  $('stopTestButton').disabled = testActionBusy || state.canStop !== true;
  $('armButton').hidden = !['Idle','SessionSetup','Calibration','Practice','BlockReady'].includes(state.state);
  $('testControls').hidden = !state.canPause && !state.canResume;
  $('disarmButton').hidden = !state.armed;
  $('recordingPath').textContent = state.recordingPath || 'Recording path unavailable';
  $('startParticipant').disabled = state.canStartParticipant !== true;
  if (!$('startDialog').hidden) updateCalibrationLookup();
  else $('createParticipant').disabled = submitting || state.canStartParticipant !== true;
  $('conditionOrder').querySelectorAll('button').forEach(button => { button.disabled = state.canStartParticipant !== true; });
  if (!intakeShown) { order = (state.conditionOrder?.length ? state.conditionOrder : conditions).slice(); intakeShown = true; }
  renderBlocks();
  renderDurations();
  workflows?.render(state);
  renderTracking();
  if (!$('workspace').hidden) renderImage();
  }
  function renderTracking() {
    const tracking = state?.tracking || {};
  $('headTracking').textContent = tracking.head?.valid ? 'Valid signal' : 'No signal';
  $('weaponTracking').textContent = tracking.weapon?.valid ? 'Valid signal' : 'No signal';
  if (!dragging && !orbitPending && !orbitDirty && performance.now() > orbitLocalUntil) { const camera = state?.camera || orbit;
  orbit = { yaw: Number(camera.yaw || 0), pitch: Math.max(-10, Math.min(80, Number(camera.pitch || 0))), distance: Math.max(1.5, Math.min(20, Number(camera.distance || 20))) }; }
    $('cameraInfo').textContent = `${Math.round(orbit.yaw)}° · ${Math.round(orbit.pitch)}° · ${orbit.distance.toFixed(1)} m`;
  }
  function renderImage() {
    const version = state?.viewVersion;
    if (!version || version === viewVersion || imageLoading) return;
    // Keep the previous frame visible until its replacement has decoded.
    imageLoading = true;
    const frame = new Image();
    frame.onload = () => {
      $('viewImage').src = frame.src; $('viewImage').hidden = false;
      $('viewPlaceholder').hidden = true;
      $('viewVersion').textContent = `Frame ${version}`;
      viewVersion = version; imageLoading = false;
    };
    frame.onerror = () => { imageLoading = false; };
    frame.src = `/api/view.jpg?token=${encodeURIComponent(token)}&v=${encodeURIComponent(version)}`;
  }
  async function refresh() {
    if (document.hidden) return;
    // A command's follow-up must observe a request made after that command, even
    // when the regular preview poll was already in flight.
    if (pollBusy) { await refreshIdle; return refresh(); }
  pollBusy = true;
  let finishRefresh;
  refreshIdle = new Promise(resolve => { finishRefresh = resolve; });
  const panePixels = $('viewport').clientWidth * window.devicePixelRatio;
  const preview = $('workspace').hidden ? '0' : panePixels <= 640 ? 'small' : panePixels <= 960 ? 'medium' : '1';
  try { const response = await fetch('/api/state?preview=' + preview, { headers: { 'X-Elts-Token': token }, cache: 'no-store' });
  if (!response.ok) throw new Error(`State request failed (${response.status}).`);
  const next = await response.json();
  if (Number.isInteger(next.profileRevision)) {
    if (next.profileRevision !== profileCache.revision) {
      const response = await fetch('/api/profiles', {headers:{'X-Elts-Token':token}, cache:'no-store'});
      if (!response.ok) throw new Error('Could not load saved participant IDs.');
      profileCache = await response.json();
    }
    next.participantCalibrations = profileCache.items;
  }
  state = next;
  setConnection(true);
  render(); }
    catch (error) { setConnection(false);
  $('phaseText').textContent = 'Unavailable';
  ['armButton','disarmButton','nextButton','abortButton','startParticipant','createParticipant','pauseButton','resumeButton','stopTestButton','confirmStopTest'].forEach(id => { $(id).disabled = true; }); workflows?.offline(); }
    finally { pollBusy = false; finishRefresh(); }
  }
  function createDurationEditor(condition) {
    const form = document.createElement('div'); form.className = 'duration-editor';
    const label = document.createElement('label'); label.className = 'field-label'; label.textContent = 'Seconds';
    const input = document.createElement('input'); input.type = 'number'; input.min = '1'; input.max = '3600'; input.step = '1'; input.required = true; input.id = `duration-${condition}`;
    input.setAttribute('aria-label', `${condition} duration in seconds`);
    label.append(input);
    const status = document.createElement('span'); status.className = 'duration-save-status'; status.id = `durationStatus-${condition}`; status.setAttribute('role', 'status');
    input.setAttribute('aria-describedby', status.id);
    form.append(label, status);
    input.addEventListener('input', () => {
      durationDrafts.add(condition); durationFeedback.set(condition, 'Unsaved change');
    });
    // Number inputs commit their value on blur; Enter makes that commit explicit.
    input.addEventListener('keydown', event => {
      if (event.key === 'Enter') { event.preventDefault(); input.blur(); }
    });
    input.addEventListener('change', async () => {
      if (!input.reportValidity() || durationSaves.has(condition)) return;
      const seconds = Number(input.value); durationSaves.set(condition, seconds); durationFeedback.set(condition, 'Saving…'); input.disabled = true;
      $('settingsError').hidden = true;
      try { await command('setDuration', {condition, seconds}); await refresh(); }
      catch (error) { durationSaves.delete(condition); durationFeedback.set(condition, 'Not saved'); input.disabled = false; notifyError($('settingsError'), error); }
    });
    return form;
  }
  function renderDurations() {
    for (const condition of conditions) {
      const input = $(`duration-${condition}`), status = $(`durationStatus-${condition}`);
      const seconds = state.testDurations?.[condition] ?? 300;
      if (durationSaves.get(condition) === seconds) {
        durationSaves.delete(condition);
        if (Number(input.value) === seconds) durationDrafts.delete(condition);
        durationFeedback.set(condition, 'Saved');
      }
      if (!durationDrafts.has(condition) && document.activeElement !== input) input.value = seconds;
      const editable = state.editableDurations?.includes(condition) === true;
      input.disabled = !editable || durationSaves.has(condition);
      const pauseToEdit = !editable && state.state === 'BlockRunning' && state.condition === condition;
      status.textContent = pauseToEdit ? 'Pause to edit' : durationFeedback.get(condition) || '';
      status.classList.toggle('is-saving', durationSaves.has(condition));
      status.classList.toggle('is-saved', status.textContent === 'Saved');
      status.classList.toggle('is-error', status.textContent === 'Not saved');
    }
  }
  async function testAction(action, payload = {}) {
    if (testActionBusy) return;
    testActionBusy = true; $('testControlError').hidden = true;
    try { await command(action, payload); await refresh(); }
    catch (error) { notifyError($('testControlError'), error); throw error; }
    finally { testActionBusy = false; render(); }
  }
  function renderOrder() { workflows?.renderOrder(); }
  // A copied assignment row carries the ID as well as the order, so a row
  // for another participant cannot be accepted through an accidental paste.
  function readAssignment(participant) {
    const raw = $('assignedRow').value.trim();
    if (!raw) return { valid: true, message: 'Paste the ID and four condition codes from the assignment sheet.' };
    const [id, ...assignedOrder] = raw.toUpperCase().split(/[\s,;]+/);
    if (!/^[0-9]{1,6}$/.test(id) || String(Number(id)) !== participant)
      return { valid: false, message: 'The row must start with the selected participant ID.' };
    if (assignedOrder.length !== 4 || new Set(assignedOrder).size !== 4 ||
        !assignedOrder.every(code => conditions.includes(code)))
      return { valid: false, message: 'Include each of the four condition codes once.' };
    const differs = assignedOrder.some((code, index) => code !== order[index]);
    const reason = differs ? $('deviationReason').value : '';
    return { valid: !differs || !!reason, differs, assignment: { participant, order: assignedOrder, reason },
      message: differs ? 'Different from Settings. Correct the configured order or select the reason to use it.'
        : 'Assignment matches Settings.' };
  }
  let pickerSignature = '';
  function updateCalibrationLookup() {
    const picker = $('participantPicker');
    const profiles = state?.participantCalibrations || [];
    const signature = JSON.stringify(profiles);
    if (signature !== pickerSignature) {
      const selected = picker.value;
      picker.replaceChildren(new Option('Choose a participant ID', ''));
      for (const profile of profiles) picker.add(new Option(String(profile.id), String(profile.id)));
      picker.value = profiles.some(profile => String(profile.id) === selected) ? selected : '';
      pickerSignature = signature;
    }
    const profile = profiles.find(entry => String(entry.id) === picker.value);
    const available = !!profile && profile.available !== false && !state.calibrationDirectoryUnavailable;
    $('calibrationStatus').textContent = state?.calibrationDirectoryUnavailable
      ? 'The calibration folder could not be read. Check access and try again.'
      : profiles.length === 0 ? 'No saved IDs. Save an ID in the calibration workspace or run PREPARE-REHEARSAL.cmd in the development package.'
      : !profile ? 'Select the ID saved in the Calibration Environment.'
      : !available ? 'This calibration cannot be read. Choose another ID or review the saved profile in the calibration workspace.'
      : profile.screenMatches === false ? 'This ID was saved with different screen settings. The station screen will be used.'
      : 'Calibration ready.';
    $('calibrationStatus').classList.toggle('is-missing', !!state?.calibrationDirectoryUnavailable || (!!profile && !available));
    $('profileSources').hidden = !available || !profile.revision;
    if (!$('profileSources').hidden) {
      const room = (state.roomRevision || 'Unavailable').replace(/^synthetic-room-([0-9a-f]{12})[0-9a-f]+$/, 'Station room $1');
      $('sourceRevisions').textContent = `participant-${profile.id}.json · revision ${profile.revision.slice(0, 12)}. Room revision · ${room}.`;
    }
    const checked = readAssignment(picker.value);
    $('assignmentStatus').textContent = checked.message;
    $('deviationField').hidden = !checked.differs;
    $('createParticipant').disabled = submitting || !available || !checked.valid || state?.canStartParticipant !== true;
  }
  function openDialog() {
    renderOrder();
    $('participantPicker').value = '';
    $('profileSources').open = false;
    $('assignmentCheck').open = false;
    $('assignedRow').value = '';
    $('deviationReason').value = '';
    $('startError').hidden = true;
    $('startDialog').hidden = false;
    updateCalibrationLookup();
    $('participantPicker').focus();
  }
  function closeDialog() { $('startDialog').hidden = true; $('startParticipant').focus(); }
  function openAbort() {
    abortRunId = state?.runId || '';
    $('abortError').hidden = true;
    $('abortDialog').hidden = false;
    $('cancelAbort').focus();
  }
  function closeAbort() {
    $('abortDialog').hidden = true;
    $('abortButton').focus();
  }
  function openSettings(event) {
    settingsOpener = event.currentTarget;
    renderOrder();
    $('settingsDialog').hidden = false;
    $('closeSettings').focus();
  }
  function wire() {
    workflows = window.createAdministratorWorkflows({ $, command, refresh, getState: () => state, getOrder: () => order,
      setOrder: value => { order = value; }, createDurationEditor, escapeHtml, formatTime });
    workflows.renderOrder();
    $('displayCheck').addEventListener('click', () => command('displayCheck').catch(e => notifyError($('settingsError'), e)));
    $('captions').addEventListener('change', () => command('captions', {enabled:$('captions').checked}).catch(e => notifyError($('settingsError'), e)));
    $('pauseButton').addEventListener('click', () => testAction('pause').catch(() => {}));
    $('resumeButton').addEventListener('click', () => testAction('resume').catch(() => {}));
    $('stopTestButton').addEventListener('click', () => {
      stopBlockId = state?.blockId || ''; stopRunId = state?.runId || ''; $('stopTestError').hidden = true;
      $('stopTestDialog').hidden = false; $('confirmStopTest').disabled = false; $('cancelStopTest').focus();
    });
    $('cancelStopTest').addEventListener('click', () => { $('stopTestDialog').hidden = true; $('stopTestButton').focus(); });
    $('confirmStopTest').addEventListener('click', async () => {
      $('confirmStopTest').disabled = true;
      try { await testAction('stopTest', {blockId:stopBlockId, runId:stopRunId}); $('stopTestDialog').hidden = true; $('nextButton').focus(); }
      catch (error) { notifyError($('stopTestError'), error); }
      finally { $('confirmStopTest').disabled = false; }
    });
    $('startParticipant').addEventListener('click', openDialog);
  $('closeDialog').addEventListener('click', closeDialog);
  $('cancelDialog').addEventListener('click', closeDialog);
  $('assignedRow').addEventListener('input', () => { $('deviationReason').value = ''; updateCalibrationLookup(); });
  $('deviationReason').addEventListener('change', updateCalibrationLookup);
  $('participantPicker').addEventListener('change', () => {
    $('deviationReason').value = '';
    $('startError').hidden = true;
    updateCalibrationLookup();
  });
  $('startForm').addEventListener('submit', async event => {
    event.preventDefault();
    updateCalibrationLookup();
    if ($('createParticipant').disabled) return;
    const participant = $('participantPicker').value;
    submitting = true;
    $('createParticipant').disabled = true;
    try {
      await command('startParticipant', { participant, conditionOrder: order, assignment: readAssignment(participant).assignment });
      closeDialog();
      await refresh();
    } catch (error) {
      notifyError($('startError'), error);
    } finally {
      submitting = false;
      updateCalibrationLookup();
    }
  });
  $('armButton').addEventListener('click', async () => { try { await command(state?.armed ? 'disarm' : 'arm');
  await refresh(); } catch (error) { $('armStatus').lastElementChild.textContent = error.message; } });
  $('disarmButton').addEventListener('click', async () => { try { await command('disarm');
  await refresh(); } catch (error) { $('armStatus').lastElementChild.textContent = error.message; } });
  $('nextButton').addEventListener('click', async () => { try { await command('next');
  await refresh(); } catch (error) { $('armStatus').lastElementChild.textContent = error.message; } });
  $('abortButton').addEventListener('click', openAbort);
  $('closeAbort').addEventListener('click', closeAbort);
  $('cancelAbort').addEventListener('click', closeAbort);
  $('abortForm').addEventListener('submit', async event => {
    event.preventDefault();
    if ($('confirmAbort').disabled) return;
    $('confirmAbort').disabled = true;
    try {
      await command('abort', {runId: abortRunId});
      closeAbort();
      await refresh();
    } catch (error) { notifyError($('abortError'), error); }
    finally { $('confirmAbort').disabled = false; }
  });
  $('settingsButton').addEventListener('click', openSettings);
  $('reviewSetup').addEventListener('click', openSettings);
  $('closeSettings').addEventListener('click', () => { $('settingsDialog').hidden = true; (settingsOpener || $('settingsButton')).focus(); });
  $('previewRate').addEventListener('change', () => command('previewRate', { fps: Number($('previewRate').value) }).catch(error => notifyError($('settingsError'), error)));
  $('reduceMotion').checked = preference.get('elts-admin-reduced-motion') === 'true';
  document.body.classList.toggle('reduce-motion', $('reduceMotion').checked);
  $('reduceMotion').addEventListener('change', () => { document.body.classList.toggle('reduce-motion', $('reduceMotion').checked); preference.set('elts-admin-reduced-motion', $('reduceMotion').checked); });
  $('windowed').addEventListener('click', () => command('windowed').catch((error) => { notifyError($('settingsError'), error); }));
  $('fullscreen').addEventListener('click', () => command('fullscreen').catch((error) => { notifyError($('settingsError'), error); }));
  // Coalesce camera changes; never queue a stream of stale orbit commands.
  const sendOrbit = async () => {
    orbitDirty = true; orbitLocalUntil = performance.now() + 700;
    if (orbitPending) return;
    orbitPending = true;
    try {
      while (orbitDirty) { orbitDirty = false; await command('viewOrbit', { ...orbit }); }
    } catch (error) { notifyError($('settingsError'), error); }
    finally { orbitPending = false; orbitLocalUntil = performance.now() + 700; }
  };
  const viewport = $('viewport');
  // Keep browser image/text dragging from interrupting the camera's pointer capture.
  viewport.addEventListener('dragstart', event => event.preventDefault());
  viewport.addEventListener('pointerdown', (event) => { if (event.button !== 0) return; dragging = true; lastPoint = event; viewport.setPointerCapture(event.pointerId); });
  viewport.addEventListener('pointermove', (event) => { if (!dragging) return;
    orbit.yaw += (event.clientX - lastPoint.clientX) * .35;
    orbit.pitch = Math.max(-10, Math.min(80, orbit.pitch + (event.clientY - lastPoint.clientY) * .25));
    lastPoint = event; sendOrbit(); renderTracking();
  });
  const endOrbit = () => { dragging = false; lastPoint = null; };
  viewport.addEventListener('pointerup', endOrbit);
  viewport.addEventListener('pointercancel', endOrbit);
  viewport.addEventListener('lostpointercapture', endOrbit);
  viewport.addEventListener('wheel', (event) => { event.preventDefault();
    orbit.distance = Math.max(1.5, Math.min(20, orbit.distance + event.deltaY * .01));
    sendOrbit(); renderTracking();
  }, { passive: false });
  viewport.addEventListener('keydown', event => {
    if (!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','+','-'].includes(event.key)) return;
    event.preventDefault();
    if (event.key === 'ArrowLeft') orbit.yaw -= 5;
    if (event.key === 'ArrowRight') orbit.yaw += 5;
    if (event.key === 'ArrowUp') orbit.pitch = Math.max(-10, orbit.pitch - 5);
    if (event.key === 'ArrowDown') orbit.pitch = Math.min(80, orbit.pitch + 5);
    if (event.key === '+') orbit.distance = Math.max(1.5, orbit.distance - .2);
    if (event.key === '-') orbit.distance = Math.min(20, orbit.distance + .2);
    sendOrbit(); renderTracking();
  });
  $('cameraDistance').addEventListener('input', () => { orbit.distance = Number($('cameraDistance').value); sendOrbit(); });
  $('resetCamera').addEventListener('click', () => { orbit = { yaw:-32, pitch:16, distance:4.8 }; $('cameraDistance').value = 4.8; command('resetView'); });
  $('splitHandle').addEventListener('pointerdown', (event) => { const workspace = $('workspace');
  const move = (e) => { const ratio = Math.max(.3, Math.min(.65, (workspace.getBoundingClientRect().right - e.clientX) / workspace.clientWidth));
  document.documentElement.style.setProperty('--pane', `${ratio * 100}%`);
  preference.set('elts-admin-pane', ratio); };
  const done = () => { window.removeEventListener('pointermove', move);
  window.removeEventListener('pointerup', done); };
  window.addEventListener('pointermove', move);
  window.addEventListener('pointerup', done); });
  $('splitHandle').addEventListener('keydown', (event) => { if (!['ArrowLeft','ArrowRight'].includes(event.key)) return;
  event.preventDefault();
  const current = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--pane')) / 100;
  const next = Math.max(.3, Math.min(.65, current + (event.key === 'ArrowLeft' ? .02 : -.02)));
  document.documentElement.style.setProperty('--pane', `${next * 100}%`);
  preference.set('elts-admin-pane', next); });
  document.addEventListener('keydown', event => {
    if (document.querySelector('dialog[open]')) return;
    const dialog = [...document.querySelectorAll('.overlay:not([hidden])')].at(-1);
    if (!dialog) return;
    if (event.key === 'Escape') {
      const cancel = dialog.querySelector('[id^="cancel"], [id^="close"]');
      if (cancel) { event.preventDefault(); cancel.click(); }
      return;
    }
    if (event.key !== 'Tab') return;
    const fields = [...dialog.querySelectorAll('button,input,textarea,select,[tabindex]')]
      .filter(field => field.tabIndex >= 0 && !field.disabled && field.getClientRects().length);
    const first = fields[0], last = fields.at(-1);
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
  });
  }
  if (savedPane >= .3 && savedPane <= .65) document.documentElement.style.setProperty('--pane', `${savedPane * 100}%`);
  wire();
  dataViewer = window.createDataViewer({command, refresh, escapeHtml, token});
  refresh();
  // Match the selected preview cadence without overlapping state requests.
  const poll = async () => {
    await refresh();
    setTimeout(poll, document.hidden ? 1000 : 1000 / Math.max(10, Math.min(30, state?.previewFps || 10)));
  };
  setTimeout(poll, 100);
})();
