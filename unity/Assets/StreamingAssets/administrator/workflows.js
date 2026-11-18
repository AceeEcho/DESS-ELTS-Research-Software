/* Administrator workflows share the existing authenticated command bridge.
 * UI preferences contain test settings only, never participant details or notes. */
window.createAdministratorWorkflows = ({ $, command, refresh, getState, getOrder, setOrder, createDurationEditor, escapeHtml, formatTime }) => {
  const presetKey = 'elts-admin-test-presets-v1';
  const motion = () => !document.body.classList.contains('reduce-motion') && !matchMedia('(prefers-reduced-motion: reduce)').matches;
  let setupBusy = false, actionBusy = false, drag = null, scrollFrame = 0;
  let phaseDirty = false, phaseSaving = false, exceptionContext = null;
  let removalRequest = '', removalContext, online = false;
  let presets = {};
  try { presets = JSON.parse(localStorage.getItem(presetKey) || '{}'); } catch { /* A bad preference cannot block testing. */ }
  if (!presets || typeof presets !== 'object' || Array.isArray(presets)) presets = {};
  const error = (id, value) => { $(id).textContent = value.message || String(value); $(id).hidden = false; };
  const html = (id, value) => { if ($(id).innerHTML !== value) $(id).innerHTML = value; };
  const setup = () => ({ conditionOrder: getOrder().slice(), durations: { ...getState().testDurations },
    practiceSeconds: getState().tools.practiceSeconds, breakSeconds: getState().tools.breakSeconds });

  async function applySetup(value) {
    if (setupBusy || !getState()?.canStartParticipant) return;
    setupBusy = true; $('settingsError').hidden = true; $('orderStatus').textContent = 'Saving setup…';
    try {
      await command('applySetup', value);
      await refresh();
      setOrder(getState().conditionOrder.slice());
      $('orderStatus').textContent = 'Setup saved.';
    } catch (e) {
      setOrder(getState().conditionOrder.slice());
      error('settingsError', e); $('orderStatus').textContent = 'Setup was not changed.';
    } finally { setupBusy = false; renderOrder(); }
  }

  // FLIP animation: measure before a reorder, commit layout, then animate each
  // displaced tile from its old position into the new layout without layout loops.
  function tileRects() {
    return new Map([...$('conditionOrder').querySelectorAll('.order-item:not(.is-dragging)')]
      .map(tile => [tile, tile.getBoundingClientRect()]));
  }
  function slideTiles(before) {
    if (!motion()) return;
    before.forEach((_, tile) => tile.getAnimations().forEach(animation => animation.cancel()));
    before.forEach((rect, tile) => {
      const after = tile.getBoundingClientRect();
      if (rect.top !== after.top) tile.animate([{ transform: `translateY(${rect.top - after.top}px)` }, { transform: 'translateY(0)' }],
        { duration: 250, easing: 'cubic-bezier(.2,.85,.2,1)' });
    });
  }
  function renumber() {
    [...$('conditionOrder').querySelectorAll('.order-item')].forEach((tile, index) => {
      tile.querySelector('.order-num').textContent = String(index + 1).padStart(2, '0');
    });
  }
  function renderOrder() {
    if (drag) return;
    const list = $('conditionOrder'), order = getOrder();
    // Reuse tiles so a state poll cannot remove keyboard focus or cancel a drag.
    for (const [index, condition] of order.entries()) {
      let tile = [...list.children].find(child => child.dataset.condition === condition);
      if (!tile) {
        tile = document.createElement('li'); tile.className = 'order-item'; tile.dataset.condition = condition;
        tile.innerHTML = `<button type="button" class="grab-handle" aria-label="Reorder ${escapeHtml(condition)} with the up and down arrow keys" title="Drag or use ↑ / ↓ to reorder"><span aria-hidden="true">⠿</span></button><span class="order-num"></span><strong>${escapeHtml(condition)}<small>${condition.endsWith('_FT') ? 'Fixed targets' : 'Moving targets'}</small></strong>`;
        tile.append(createDurationEditor(condition));
      }
      if (list.children[index] !== tile) list.insertBefore(tile, list.children[index] || null);
      tile.querySelector('.grab-handle').disabled = !getState()?.canStartParticipant || setupBusy;
    }
    renumber();
  }
  function moveTile(condition, delta) {
    if (drag || setupBusy || !getState()?.canStartParticipant) return;
    const order = getOrder().slice(), from = order.indexOf(condition), to = from + delta;
    if (to < 0 || to >= order.length) return;
    const before = tileRects();
    [order[from], order[to]] = [order[to], order[from]]; setOrder(order); renderOrder(); slideTiles(before);
    $('conditionOrder').querySelector(`[data-condition="${condition}"] .grab-handle`).focus();
    applySetup(setup());
  }
  $('conditionOrder').addEventListener('keydown', event => {
    if (!event.target.matches('.grab-handle') || !['ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault(); moveTile(event.target.closest('.order-item').dataset.condition, event.key === 'ArrowUp' ? -1 : 1);
  });
  $('conditionOrder').addEventListener('pointerdown', event => {
    const handle = event.target.closest('.grab-handle');
    if (!handle || event.button !== 0 || setupBusy || !getState()?.canStartParticipant) return;
    event.preventDefault();
    const tile = handle.closest('.order-item'), rect = tile.getBoundingClientRect();
    const placeholder = document.createElement('li'); placeholder.className = 'order-placeholder';
    placeholder.style.height = `${rect.height}px`; placeholder.setAttribute('aria-hidden', 'true');
    tile.before(placeholder);
    drag = { tile, handle, placeholder, original: getOrder().slice(), startX: event.clientX, startY: event.clientY,
      x: event.clientX, y: event.clientY, rect, pointerId: event.pointerId };
    tile.classList.add('is-dragging');
    Object.assign(tile.style, { width: `${rect.width}px`, height: `${rect.height}px`, left: `${rect.left}px`, top: `${rect.top}px` });
    handle.setPointerCapture(event.pointerId); handle.focus();
    $('orderStatus').textContent = `Moving ${tile.dataset.condition} · Esc to cancel`;
    scrollFrame = requestAnimationFrame(scrollDrag);
  });
  function placePlaceholder() {
    if (!drag) return;
    const { tile, placeholder } = drag;
    const candidates = [...$('conditionOrder').children].filter(child => child !== tile && child !== placeholder);
    const listTop = $('conditionOrder').getBoundingClientRect().top;
    const target = candidates.find(child => drag.y < listTop + child.offsetTop + child.offsetHeight / 2);
    if (target ? placeholder.nextElementSibling === target : placeholder === $('conditionOrder').lastElementChild) return;
    const before = tileRects();
    if (target) target.before(placeholder); else $('conditionOrder').append(placeholder);
    slideTiles(before);
  }
  function scrollDrag() {
    if (!drag) return;
    const modal = $('settingsDialog').querySelector('.modal'), rect = modal.getBoundingClientRect();
    const edge = 64;
    const step = drag.y < rect.top + edge ? -Math.min(12, (rect.top + edge - drag.y) / 5)
      : drag.y > rect.bottom - edge ? Math.min(12, (drag.y - rect.bottom + edge) / 5) : 0;
    if (step) { modal.scrollTop += step; placePlaceholder(); }
    scrollFrame = requestAnimationFrame(scrollDrag);
  }
  window.addEventListener('pointermove', event => {
    if (!drag || event.pointerId !== drag.pointerId) return;
    drag.x = event.clientX; drag.y = event.clientY;
    drag.tile.style.top = `${drag.rect.top + drag.y - drag.startY}px`;
    drag.tile.style.left = `${drag.rect.left + (drag.x - drag.startX) * .15}px`;
    placePlaceholder();
  });
  function finishDrag(cancel = false) {
    if (!drag) return;
    cancelAnimationFrame(scrollFrame);
    const { tile, placeholder, original, handle, pointerId } = drag;
    const lifted = tile.getBoundingClientRect();
    placeholder.before(tile); placeholder.remove();
    const order = cancel ? original : [...$('conditionOrder').children].map(child => child.dataset.condition);
    drag = null; tile.classList.remove('is-dragging'); tile.removeAttribute('style');
    if (handle.hasPointerCapture(pointerId)) handle.releasePointerCapture(pointerId);
    setOrder(order); renderOrder();
    if (!cancel && motion()) {
      const at = tile.getBoundingClientRect();
      tile.animate([{ transform: `translate(${lifted.left - at.left}px,${lifted.top - at.top}px) scale(1.025)`, boxShadow: '0 18px 40px #0008' },
        { transform: 'translate(0,0) scale(1)', boxShadow: '0 2px 5px #0002' }], { duration: 280, easing: 'cubic-bezier(.2,.85,.2,1)' });
    }
    handle.focus();
    if (!cancel && order.join() !== original.join()) applySetup(setup());
    else $('orderStatus').textContent = cancel ? 'Move canceled.' : 'Order unchanged.';
  }
  window.addEventListener('pointerup', event => { if (drag?.pointerId === event.pointerId) finishDrag(); });
  window.addEventListener('pointercancel', () => finishDrag(true));
  window.addEventListener('blur', () => finishDrag(true));
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && drag) { event.preventDefault(); event.stopImmediatePropagation(); finishDrag(true); }
  }, true);

  function renderPresets() {
    const selected = $('presetSelect').value;
    $('presetSelect').innerHTML = '<option value="">Choose a setup</option>' + Object.keys(presets).sort().map(name =>
      `<option value="${escapeHtml(name)}">${escapeHtml(name)}</option>`).join('');
    $('presetSelect').value = selected;
  }
  function persistPresets(next) {
    $('settingsError').hidden = true;
    try { localStorage.setItem(presetKey, JSON.stringify(next)); }
    catch (e) { error('settingsError', new Error('This browser could not save presets. ' + e.message)); $('orderStatus').textContent = ''; return false; }
    presets = next;
    renderPresets();
    return true;
  }
  $('savePreset').addEventListener('click', () => {
    const name = $('presetName').value.trim();
    if (!name) { error('settingsError', new Error('Enter a name for this setup.')); return; }
    // Only the server's applied values are saved, never unsaved input drafts.
    if (!persistPresets({ ...presets, [name]: setup() })) return; $('presetSelect').value = name; $('orderStatus').textContent = `Saved setup “${name}”.`;
  });
  $('loadPreset').addEventListener('click', () => {
    const value = presets[$('presetSelect').value];
    if (value) applySetup(value);
  });
  $('deletePreset').addEventListener('click', () => { const next = { ...presets }; delete next[$('presetSelect').value]; persistPresets(next); });
  const phaseInputs = [$('practiceSeconds'), $('breakSeconds')];
  function phaseValues() { return phaseInputs.map(input => input.value); }
  async function savePhases() {
    if (phaseSaving || !phaseDirty || !$('phaseTimingForm').reportValidity()) return;
    const [practice, rest] = phaseValues();
    phaseSaving = true;
    $('settingsError').hidden = true;
    $('phaseSaveStatus').textContent = 'Saving…';
    let saved = false;
    try {
      await command('setPhaseDurations', {practiceSeconds:Number(practice), breakSeconds:Number(rest)});
      // Keep a newer edit if the operator tabbed to the next field during this save.
      phaseDirty = phaseValues().join() !== [practice, rest].join();
      await refresh();
      $('phaseSaveStatus').textContent = phaseDirty ? 'Unsaved change' : 'Saved';
      saved = true;
    } catch (e) { error('settingsError', e); $('phaseSaveStatus').textContent = 'Not saved'; }
    finally {
      phaseSaving = false;
      if (saved && phaseDirty && !phaseInputs.includes(document.activeElement)) savePhases();
    }
  }
  phaseInputs.forEach(input => {
    input.addEventListener('input', () => { phaseDirty = true; $('phaseSaveStatus').textContent = 'Unsaved change'; });
    input.addEventListener('change', savePhases);
    input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); input.blur(); } });
  });
  $('phaseTimingForm').addEventListener('submit', event => { event.preventDefault(); savePhases(); });
  async function action(name, payload = {}) {
    if (actionBusy) return;
    actionBusy = true; $('testControlError').hidden = true;
    try {
      const context = name.startsWith('tutorial') ? { tutorialRevision: getState().tutorial.revision } : {};
      await command(name, { ...context, ...payload }); await refresh();
    }
    catch (e) { error('testControlError', e); throw e; }
    finally { actionBusy = false; render(getState()); }
  }
  ['tutorialRedo', 'tutorialPause', 'tutorialResume', 'tutorialReplay', 'tutorialStopDemo', 'tutorialRepeatDemo',
    'tutorialRestartPractice', 'tutorialFinishPractice', 'startBreak', 'skipBreak']
    .forEach(id => $(id).addEventListener('click', () => action(id).catch(() => {})));
  $('tutorialPrimary').addEventListener('click', () => {
    const tutorial = getState()?.tutorial;
    if (tutorial?.canPrimary) action(tutorial.primaryAction).catch(() => {});
  });
  // Manual mode cannot be undone for this participant, so it is confirmed first.
  $('tutorialManual').addEventListener('click', () => {
    if (!getState()?.tutorial?.canManual) return;
    $('manualError').hidden = true; $('manualDialog').hidden = false; $('cancelManual').focus();
  });
  $('cancelManual').addEventListener('click', () => { $('manualDialog').hidden = true; $('tutorialManual').focus(); });
  $('confirmManual').addEventListener('click', async () => {
    $('confirmManual').disabled = true;
    try { await action('tutorialManual'); $('manualDialog').hidden = true; }
    catch (e) { error('manualError', e); }
    finally { $('confirmManual').disabled = false; }
  });
  function openException(kind) {
    const state = getState();
    exceptionContext = { kind, runId: state.runId, blockId: state.blockId };
    $('exceptionTitle').textContent = kind === 'skipTest' ? `Skip ${state.condition}?` : 'Repeat a finished test';
    $('exceptionHelp').textContent = kind === 'skipTest' ? 'The reason is saved with the skipped test.'
      : 'The new attempt runs next. Earlier results are kept.';
    $('repeatChoice').hidden = kind !== 'repeatTest';
    $('repeatCondition').innerHTML = (state.tools.repeatable || []).map(c => `<option>${escapeHtml(c)}</option>`).join('');
    $('exceptionReason').value = ''; $('reasonLength').textContent = '256 characters available'; $('exceptionError').hidden = true;
    $('exceptionDialog').hidden = false; $('exceptionReason').focus();
  }
  $('skipTest').addEventListener('click', () => openException('skipTest'));
  $('repeatTest').addEventListener('click', () => openException('repeatTest'));
  $('cancelException').addEventListener('click', () => { $('exceptionDialog').hidden = true; $(exceptionContext?.kind || 'skipTest').focus(); });
  $('exceptionReason').addEventListener('input', () => {
    $('reasonLength').textContent = (256 - $('exceptionReason').value.length) + ' characters available';
  });
  $('exceptionForm').addEventListener('submit', async event => {
    event.preventDefault(); const reason = $('exceptionReason').value.trim();
    if (!reason || !exceptionContext) return;
    $('confirmException').disabled = true;
    try {
      await action(exceptionContext.kind, { ...exceptionContext, reason, condition: $('repeatCondition').value });
      $('exceptionDialog').hidden = true;
    } catch (e) { error('exceptionError', e); }
    finally { $('confirmException').disabled = false; }
  });

  function removalControls(state) {
    const result = state?.datasetRemoval;
    if (removalRequest && result?.request === removalRequest) {
      $('datasetFeedback').textContent = result.message;
      if (result.state === 'error' || result.state === 'complete') {
        removalRequest = '';
        $('datasetConfirmation').value = '';
        $('datasetStatus').textContent = result.message;
        if (result.state === 'complete') $('datasetDialog').close();
      }
    }
    const allowed = online && state?.canStartParticipant && !state.data?.busy && !removalRequest;
    $('openDatasetRemoval').disabled = !allowed;
    const sameDataset = removalContext && removalContext.runId === (state?.runId || '') &&
      removalContext.version === (result?.version || 0);
    $('confirmDatasetRemoval').disabled = !allowed || !sameDataset || $('datasetConfirmation').value !== 'CONFIRM';
    $('datasetConfirmation').disabled = !!removalRequest;
    $('cancelDatasetRemoval').disabled = !!removalRequest;
  }
  $('openDatasetRemoval').addEventListener('click', () => {
    const state = getState();
    removalContext = {runId: state.runId || '', version: state.datasetRemoval?.version || 0};
    $('datasetConfirmation').value = '';
    $('datasetFeedback').textContent = '';
    removalControls(getState());
    $('datasetDialog').showModal();
  });
  $('cancelDatasetRemoval').addEventListener('click', () => $('datasetDialog').close());
  $('datasetDialog').addEventListener('cancel', event => { if (removalRequest) event.preventDefault(); });
  $('datasetConfirmation').addEventListener('input', () => removalControls(getState()));
  $('confirmDatasetRemoval').addEventListener('click', async () => {
    if ($('confirmDatasetRemoval').disabled || removalRequest) return;
    removalRequest = crypto.randomUUID();
    $('datasetFeedback').textContent = 'Requesting removal…';
    removalControls(getState());
    try {
      await command('wipeDataset', {...removalContext, confirmation: $('datasetConfirmation').value, request: removalRequest});
      await refresh();
    } catch (e) {
      removalRequest = '';
      $('datasetConfirmation').value = '';
      $('datasetFeedback').textContent = e.message;
      removalControls(getState());
    }
  });
  function render(state) {
    online = true;
    removalControls(state);
    if (!state?.tools) return;
    const tools = state.tools, tutorial = state.tutorial;
    if (drag && !state.canStartParticipant) finishDrag(true);
    if (!drag && !setupBusy) {
      if (getOrder().join() !== state.conditionOrder.join()) setOrder(state.conditionOrder.slice());
      renderOrder();
    }
    $('setupWelcome').hidden = !state.canStartParticipant;
    if (state.canStartParticipant) {
      html('plannedTests', state.conditionOrder.map(code => `<li><strong>${escapeHtml(code)}</strong> ${formatTime(state.testDurations?.[code] ?? 300)}</li>`).join(''));
      $('plannedBreaks').textContent = `Practice ${formatTime(tools.practiceSeconds)} · Breaks ${formatTime(tools.breakSeconds)}`;
    }
    $('orderLocked').hidden = state.canStartParticipant;
    renderTutorial(state, tutorial);
    $('practicePanel').hidden = !tutorial?.canRestartPractice && tutorial?.stage !== 'PracticeRunning';
    $('practiceTimer').textContent = state.state === 'Practice' ? formatTime(Math.ceil(state.remainingSeconds)) : 'Finished';
    $('tutorialRestartPractice').disabled = actionBusy || !tutorial?.canRestartPractice;
    $('tutorialFinishPractice').disabled = actionBusy || !tutorial?.canFinishPractice;
    const waitingForBreak = state.state === 'BlockEnded';
    $('breakPanel').hidden = !waitingForBreak && state.state !== 'Break';
    $('startBreak').hidden = !waitingForBreak;
    $('startBreak').disabled = actionBusy || !tools.canStartBreak;
    $('breakTimer').textContent = formatTime(Math.ceil(waitingForBreak ? tools.breakSeconds : state.remainingSeconds));
    const breakHelp = $('breakHelp');
    breakHelp.textContent = state.busy ? 'Saving results…' : waitingForBreak
      ? (tools.automaticStudy ? 'Break starts automatically.' : '')
      : tools.queueIndex + 1 >= tools.schedule.length ? 'Session ends after this break.'
      : (tools.automaticStudy ? 'Next test starts automatically after the transition.' : '');
    breakHelp.hidden = !breakHelp.textContent;
    $('skipBreak').disabled = actionBusy || !tools.canSkipBreak;
    $('skipTest').disabled = actionBusy || !tools.canSkipTest;
    $('repeatTest').disabled = actionBusy || !tools.canRepeat || !tools.repeatable?.length;
    $('saveStatus').textContent = tools.saveStatus;
    const latestSave = tools.latestCheckpoint;
    if (tools.saveStatus.startsWith('Saved ') && latestSave?.savedUtc) {
      const time = new Date(latestSave.savedUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
      $('saveStatus').textContent = `${latestSave.condition} saved at ${time} · ${latestSave.status}`;
    }
    $('saveStatus').classList.toggle('save-failed', /failed/i.test(tools.saveStatus));
    html('readinessList', tools.readiness.map(item => `<div class="readiness-item"><div><strong>${escapeHtml(item.name)}</strong><span class="readiness-${item.status.toLowerCase()}">${escapeHtml(item.status)}</span></div><p>${escapeHtml(item.detail)}</p></div>`).join(''));
    if (!phaseDirty) { $('practiceSeconds').value = tools.practiceSeconds; $('breakSeconds').value = tools.breakSeconds; }
    phaseInputs.forEach(input => { input.disabled = !!state.busy; });
    $('loadPreset').disabled = setupBusy || !state.canStartParticipant;
    $('savePreset').disabled = setupBusy || !state.canStartParticipant;
  }
  function offline() {
    online = false;
    removalControls(null);
    finishDrag(true);
    $('conditionOrder').querySelectorAll('button').forEach(button => { button.disabled = true; });
    ['tutorialPrimary', 'tutorialManual', 'confirmManual', 'tutorialRedo', 'tutorialPause', 'tutorialResume', 'tutorialReplay', 'tutorialStopDemo', 'tutorialRepeatDemo',
      'tutorialRestartPractice', 'tutorialFinishPractice', 'startBreak', 'skipBreak', 'skipTest', 'repeatTest', 'practiceSeconds', 'breakSeconds', 'loadPreset', 'savePreset']
      .forEach(id => { $(id).disabled = true; });
  }
  function renderTutorial(state, tutorial) {
    $('tutorialPanel').hidden = !tutorial || !state.participant || ['Aborted', 'Failed'].includes(state.state);
    if (!tutorial) return;
    const step = tutorial.step ? ` · ${tutorial.step.replaceAll('-', '.')}` : '';
    $('tutorialStep').textContent = tutorial.manual ? `MANUAL MODE${step}` : tutorial.step ? `WALKTHROUGH${step}` : 'PARTICIPANT WALKTHROUGH';
    $('tutorialTitle').textContent = tutorial.started ? tutorial.title || 'Scenario running' : 'Setup, calibration and practice';
    $('tutorialInstruction').textContent = tutorial.started ? tutorial.instruction : '';
    $('tutorialInstruction').hidden = !$('tutorialInstruction').textContent;
    $('tutorialStatus').textContent = tutorial.status || '';
    $('tutorialStatus').hidden = !tutorial.status;
    // Narration is optional per step; say which applies rather than a fixed label.
    $('tutorialAudio').textContent = !tutorial.started ? '' : tutorial.audioAvailable ? 'Narration on' :
      tutorial.audioEnabled ? 'No narration for this step' : 'Narration off';
    $('tutorialAudio').hidden = !$('tutorialAudio').textContent;
    $('tutorialManual').hidden = !tutorial.canManual;
    $('tutorialManual').disabled = actionBusy || !tutorial.canManual;
    $('tutorialPrimary').textContent = tutorial.primaryLabel || 'Waiting for session event';
    $('tutorialPrimary').hidden = !tutorial.primaryLabel;
    $('tutorialPrimary').disabled = actionBusy || !tutorial.canPrimary;
    const controls = { tutorialRedo: tutorial.canRedo, tutorialPause: tutorial.canPause, tutorialResume: tutorial.canResume,
      tutorialReplay: tutorial.canReplay, tutorialStopDemo: tutorial.demoActive, tutorialRepeatDemo: tutorial.canRepeatDemo };
    for (const [id, available] of Object.entries(controls)) { $(id).hidden = !available; $(id).disabled = actionBusy || !available; }
    $('tutorialPause').textContent = tutorial.stage === 'PracticeRunning' ? 'Pause practice' : 'Pause instruction';
    $('tutorialCalibrationReview').hidden = !tutorial.calibrationReview;
    $('tutorialCalibrationReview').textContent = tutorial.calibrationReview || '';
    const checks = [['Head review', tutorial.headAccepted], ['Gun review', tutorial.gunAccepted],
      ['Firing tried', tutorial.fired], ['Reload tried', tutorial.reloaded]];
    // Manual mode skips these checks, so they are not shown as outstanding.
    const showChecks = tutorial.started && !tutorial.manual;
    $('tutorialChecks').hidden = !showChecks;
    html('tutorialChecks', showChecks ? checks.map(([label, complete]) =>
      `<span class="step-check ${complete ? 'is-complete' : ''}"><span aria-hidden="true">${complete ? '✓' : '○'}</span> ${label}</span>`).join('') : '');
  }
  renderPresets();
  return { render, renderOrder, offline };
};
