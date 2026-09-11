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
  let testActionBusy = false, stopBlockId = '', stopRunId = '';
  const durationDrafts = new Set(), durationSaves = new Map(), durationFeedback = new Map();
  let state = null, order = conditions.slice(), pollBusy = false, viewVersion = null;
  let workflows, dataViewer;
  let refreshIdle = Promise.resolve();
  let orbit = { yaw: 0, pitch: 0, distance: 1 }, dragging = false, lastPoint = null;
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
  $('participant').textContent = state.participant || '—';
  $('runId').textContent = state.runId ? `Run ${state.runId}` : 'No run';
  $('phaseText').textContent = titleCase(phase);
  $('condition').textContent = state.condition || 'No condition selected';
  $('remaining').textContent = state.remainingSeconds == null ? '—' : formatTime(state.remainingSeconds);
  $('remaining').hidden = !['BlockReady','BlockRunning','BlockPaused'].includes(state.state);
  if(state.state === 'BlockReady') $('remaining').textContent = formatTime(state.durationSeconds);
  $('elapsed').textContent = formatTime(state.activeSeconds);
  $('testTiming').textContent = `Test duration: ${formatTime(state.durationSeconds || 300)} · edit individual durations in Settings`;
  $('shots').textContent = state.shots ?? 0;
  $('hits').textContent = state.hits ?? 0;
  $('accuracy').textContent = state.shots ? `${Math.round((Number(state.hits || 0) / Number(state.shots)) * 100)}%` : '—';
  const badge = $('stateBadge');
  badge.textContent = titleCase(state.state || phase).toUpperCase();
  badge.className = `badge ${state.armed ? 'armed' : running ? 'running' : /abort|error/i.test(phase) ? 'error' : ''}`;
  const armText = state.state === 'BlockEnded' ? (state.busy ? 'Test finished · saving results.' : 'Test saved · start the break when ready.') : state.state === 'Break' ? 'Break in progress · arm the next test after the timer ends.' : state.canResume ? 'Paused · timer and targets held' : state.canPause ? 'Test running · pause or finish this test below' : state.armed ? 'ARMED · waiting for first shot' : state.canArm ? 'Ready to arm this condition' : state.message || (state.participant ? 'Preparing participant pipeline…' : 'Waiting for participant');
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
  $('createParticipant').disabled = submitting || state.canStartParticipant !== true;
  $('conditionOrder').querySelectorAll('button').forEach(button => { button.disabled = state.canStartParticipant !== true; });
  if (!intakeShown) { order = (state.conditionOrder?.length ? state.conditionOrder : conditions).slice(); intakeShown = true; }
  $('saveNote').disabled = !state.participant || !!state.busy || state.canStartParticipant;
  renderBlocks();
  renderDurations();
  workflows?.render(state);
  renderTracking();
  renderImage();
  }
  function renderTracking() {
    const tracking = state?.tracking || {};
  $('headTracking').textContent = tracking.head?.valid ? 'Valid signal' : 'No signal';
  $('weaponTracking').textContent = tracking.weapon?.valid ? 'Valid signal' : 'No signal';
  if (!dragging && !orbitPending && !orbitDirty && performance.now() > orbitLocalUntil) { const camera = state?.camera || orbit;
  orbit = { yaw: Number(camera.yaw || 0), pitch: Math.max(-10, Math.min(80, Number(camera.pitch || 0))), distance: Math.max(1.5, Math.min(12, Number(camera.distance || 4))) }; }
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
  try { const response = await fetch(`/api/state${token ? `?token=${encodeURIComponent(token)}` : ''}`, { headers: { 'X-Elts-Token': token }, cache: 'no-store' });
  if (!response.ok) throw new Error(`State request failed (${response.status}).`);
  state = await response.json();
  setConnection(true);
  render(); }
    catch (error) { setConnection(false);
  $('phaseText').textContent = 'Unavailable';
  ['armButton','disarmButton','nextButton','abortButton','startParticipant','createParticipant','saveNote','pauseButton','resumeButton','stopTestButton','confirmStopTest'].forEach(id => { $(id).disabled = true; }); workflows?.offline(); }
    finally { pollBusy = false; finishRefresh(); }
  }
  function initializeDurations() {
    for (const condition of conditions) {
      const form = document.createElement('div'); form.className = 'duration-editor';
      const label = document.createElement('label'); label.className = 'field-label'; label.textContent = `${condition} · seconds`;
      const input = document.createElement('input'); input.type = 'number'; input.min = '1'; input.max = '3600'; input.step = '1'; input.required = true; input.id = `duration-${condition}`;
      label.append(input);
      const status = document.createElement('span'); status.className = 'duration-save-status'; status.id = `durationStatus-${condition}`; status.setAttribute('role', 'status');
      form.append(label, status); $('durationEditors').append(form);
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
    }
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
      status.textContent = durationFeedback.get(condition) || '';
      status.classList.toggle('is-saving', durationSaves.has(condition));
      status.classList.toggle('is-saved', durationFeedback.get(condition) === 'Saved');
      status.classList.toggle('is-error', durationFeedback.get(condition) === 'Not saved');
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
  function openDialog() {
  renderOrder();
  $('participantInput').value = '';
  $('participantName').value = ''; $('initialNotes').value = '';
  $('startError').hidden = true;
  $('startDialog').hidden = false;
  $('participantInput').focus(); }
  function closeDialog() { $('startDialog').hidden = true; $('startParticipant').focus(); }
  function openAbort() { $('abortReason').value = '';
  $('abortError').hidden = true;
  $('abortDialog').hidden = false;
  $('abortReason').focus(); }
  function closeAbort() { $('abortDialog').hidden = true; }
  function wire() {
    workflows = window.createAdministratorWorkflows({ $, command, refresh, getState: () => state, getOrder: () => order,
      setOrder: value => { order = value; }, escapeHtml, formatTime });
    initializeDurations();
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
  $('startForm').addEventListener('submit', async (event) => { event.preventDefault();
  const participant = $('participantInput').value.trim();
  if (!participant) { $('startError').textContent = 'Participant ID is required.';
  $('startError').hidden = false;
  return; } if (submitting) return; submitting = true; $('createParticipant').disabled = true;
  try { await command('startParticipant', { participant, participantName: $('participantName').value.trim(), initialNotes: $('initialNotes').value.trim(), conditionOrder: order });
  closeDialog();
  await refresh(); } catch (error) { notifyError($('startError'), error); } finally { submitting = false; $('createParticipant').disabled = state?.canStartParticipant !== true; } });
  $('armButton').addEventListener('click', async () => { try { await command(state?.armed ? 'disarm' : 'arm');
  await refresh(); } catch (error) { $('armStatus').lastElementChild.textContent = error.message; } });
  $('disarmButton').addEventListener('click', async () => { try { await command('disarm');
  await refresh(); } catch (error) { $('armStatus').lastElementChild.textContent = error.message; } });
  $('nextButton').addEventListener('click', async () => { try { await command('next');
  await refresh(); } catch (error) { $('armStatus').lastElementChild.textContent = error.message; } });
  $('abortButton').addEventListener('click', openAbort);
  $('closeAbort').addEventListener('click', closeAbort);
  $('cancelAbort').addEventListener('click', closeAbort);
  $('abortForm').addEventListener('submit', async (event) => { event.preventDefault();
  const reason = $('abortReason').value.trim();
  if (!reason) { $('abortError').textContent = 'A reason is required.';
  $('abortError').hidden = false;
  return; } try { await command('abort', { reason });
  closeAbort();
  await refresh(); } catch (error) { notifyError($('abortError'), error); } });
  $('saveNote').addEventListener('click', async () => { const text = $('noteText').value.trim();
  if (!text) return;
  try { await command('note', { text });
  $('noteText').value = ''; $('noteText').setCustomValidity(''); } catch (error) { $('noteText').setCustomValidity(error.message);
  $('noteText').reportValidity(); } });
  $('saveNote').disabled = !state?.participant;
  $('settingsButton').addEventListener('click', () => { renderOrder(); $('settingsDialog').hidden = false; $('closeSettings').focus(); });
  $('closeSettings').addEventListener('click', () => { $('settingsDialog').hidden = true; $('settingsButton').focus(); });
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
    orbit.distance = Math.max(1.5, Math.min(12, orbit.distance + event.deltaY * .01));
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
    if (event.key === '-') orbit.distance = Math.min(12, orbit.distance + .2);
    sendOrbit(); renderTracking();
  });
  $('cameraDistance').addEventListener('input', () => { orbit.distance = Number($('cameraDistance').value); sendOrbit(); });
  $('resetCamera').addEventListener('click', () => { orbit = { yaw:145, pitch:20, distance:4 }; $('cameraDistance').value = 4; sendOrbit(); });
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
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape') { closeDialog();
  closeAbort(); $('stopTestDialog').hidden = true; $('settingsDialog').hidden = true; $('exceptionDialog').hidden = true; $('recordingsDialog').hidden = true; $('settingsButton').focus(); }
  // Keep keyboard navigation inside the topmost open modal.
  if (event.key === 'Tab') {
    const dialog = [...document.querySelectorAll('.overlay:not([hidden])')].at(-1);
    if (!dialog) return;
    const fields = [...dialog.querySelectorAll('button:not(:disabled),input:not(:disabled),textarea,select')];
    const first = fields[0], last = fields.at(-1);
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  } });
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
