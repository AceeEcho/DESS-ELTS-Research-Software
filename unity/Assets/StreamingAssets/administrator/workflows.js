/* Administrator workflows share the existing authenticated command bridge.
 * UI preferences contain test settings only, never participant details or notes. */
window.createAdministratorWorkflows = ({ $, command, refresh, getState, getOrder, setOrder, escapeHtml, formatTime }) => {
  const presetKey = 'elts-admin-test-presets-v1';
  const motion = () => !document.body.classList.contains('reduce-motion') && !matchMedia('(prefers-reduced-motion: reduce)').matches;
  let setupBusy = false, actionBusy = false, drag = null, scrollFrame = 0;
  let phaseDirty = false, exceptionContext = null, selectedRecording = '', lastReview = '';
  let presets = {};
  try { presets = JSON.parse(localStorage.getItem(presetKey) || '{}'); } catch { /* A bad preference cannot block testing. */ }
  if (!presets || typeof presets !== 'object' || Array.isArray(presets)) presets = {};
  const error = (id, value) => { $(id).textContent = value.message || String(value); $(id).hidden = false; };
  const html = (id, value) => { if ($(id).innerHTML !== value) $(id).innerHTML = value; };
  function readableNote(value) {
    const text = String(value || '');
    if (text.startsWith('Participant details: ')) {
      try {
        const details = JSON.parse(text.slice('Participant details: '.length));
        return [`Participant: ${details.participantId}`, details.participantName ? `Name / alias: ${details.participantName}` : '',
          details.initialNotes ? `Initial observations: ${details.initialNotes}` : ''].filter(Boolean).join('\n');
      } catch { return text; }
    }
    if (text.startsWith('Synthetic calibration fixture accepted: ')) return 'Synthetic calibration fixture reviewed, accepted and saved.';
    return text;
  }
  const setup = () => ({ conditionOrder: getOrder().slice(), durations: { ...getState().testDurations },
    practiceSeconds: getState().tools.practiceSeconds, breakSeconds: getState().tools.breakSeconds });

  async function applySetup(value) {
    if (setupBusy || !getState()?.canStartParticipant) return;
    setupBusy = true; $('settingsError').hidden = true; $('orderStatus').textContent = 'Saving setup…';
    try {
      await command('applySetup', value);
      await refresh();
      setOrder(getState().conditionOrder.slice());
      $('orderStatus').textContent = 'Setup saved for the next participant.';
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
        tile.innerHTML = `<button type="button" class="grab-handle" aria-label="Drag ${escapeHtml(condition)} or use arrow keys" title="Drag to reorder"><span aria-hidden="true">⠿</span></button><span class="order-num"></span><strong>${escapeHtml(condition)}</strong>`;
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
    $('orderStatus').textContent = `Moving ${tile.dataset.condition}. Release to drop; Escape to cancel.`;
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
  function persistPresets() {
    try { localStorage.setItem(presetKey, JSON.stringify(presets)); renderPresets(); }
    catch (e) { error('settingsError', new Error('This browser could not save presets. ' + e.message)); }
  }
  $('savePreset').addEventListener('click', () => {
    const name = $('presetName').value.trim();
    if (!name) { error('settingsError', new Error('Enter a name for this setup.')); return; }
    // Only the server's applied values are saved, never unsaved input drafts.
    Object.defineProperty(presets, name, { value: setup(), configurable: true, enumerable: true, writable: true });
    persistPresets(); $('presetSelect').value = name; $('orderStatus').textContent = `Saved setup “${name}”.`;
  });
  $('loadPreset').addEventListener('click', () => {
    const value = presets[$('presetSelect').value];
    if (value) applySetup(value);
  });
  $('deletePreset').addEventListener('click', () => { delete presets[$('presetSelect').value]; persistPresets(); });
  ['practiceSeconds', 'breakSeconds'].forEach(id => $(id).addEventListener('input', () => { phaseDirty = true; }));
  $('phaseTimingForm').addEventListener('submit', async event => {
    event.preventDefault(); $('applyPhaseTiming').disabled = true;
    try {
      await command('setPhaseDurations', { practiceSeconds: Number($('practiceSeconds').value), breakSeconds: Number($('breakSeconds').value) });
      phaseDirty = false; await refresh();
    } catch (e) { error('settingsError', e); }
    finally { $('applyPhaseTiming').disabled = false; }
  });
  async function action(name, payload = {}) {
    if (actionBusy) return;
    actionBusy = true; $('testControlError').hidden = true;
    try { await command(name, payload); await refresh(); }
    catch (e) { error('testControlError', e); throw e; }
    finally { actionBusy = false; render(getState()); }
  }
  ['generateCalibration', 'redoCalibration', 'acceptCalibration', 'startPractice', 'restartPractice', 'finishPractice', 'startBreak', 'skipBreak']
    .forEach(id => $(id).addEventListener('click', () => action(id).catch(() => {})));
  function openException(kind) {
    const state = getState();
    exceptionContext = { kind, runId: state.runId, blockId: state.blockId };
    $('exceptionTitle').textContent = kind === 'skipTest' ? `Skip ${state.condition}?` : 'Repeat a finished test';
    $('exceptionHelp').textContent = kind === 'skipTest' ? 'This test will be marked Skipped with your reason. No score is assigned. The remaining tests stay queued.'
      : 'A new attempt is added next. Earlier attempts and the remaining test order are preserved.';
    $('repeatChoice').hidden = kind !== 'repeatTest';
    $('repeatCondition').innerHTML = (state.tools.repeatable || []).map(c => `<option>${escapeHtml(c)}</option>`).join('');
    $('exceptionReason').value = ''; $('exceptionError').hidden = true;
    $('exceptionDialog').hidden = false; $('exceptionReason').focus();
  }
  $('skipTest').addEventListener('click', () => openException('skipTest'));
  $('repeatTest').addEventListener('click', () => openException('repeatTest'));
  $('cancelException').addEventListener('click', () => { $('exceptionDialog').hidden = true; });
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

  async function archiveAction(name, id = '') {
    $('recordingError').hidden = true;
    try { await command(name, { id }); await refresh(); }
    catch (e) { error('recordingError', e); }
  }

  $('closeRecordings').addEventListener('click', () => { $('recordingsDialog').hidden = true; $('recordingsButton').focus(); });
  $('refreshRecordings').addEventListener('click', () => archiveAction('listRecordings'));
  $('openDataFolder').addEventListener('click', () => archiveAction('openDataFolder'));
  $('recordingList').addEventListener('click', event => {
    const button = event.target.closest('[data-recording]'); if (!button) return;
    selectedRecording = button.dataset.recording; archiveAction('reviewRecording', selectedRecording);
  });
  $('exportRecording').addEventListener('click', () => archiveAction('exportRecording', selectedRecording));
  $('downloadReview').addEventListener('click', () => {
    const review = getState().tools.review; if (!review || review.id !== selectedRecording) return;
    const columns = ['condition', 'blockId', 'status', 'hits', 'shots', 'activeSeconds', 'reason', 'scoreStatus'];
    // Quote every cell and neutralize spreadsheet formulas in operator-authored text.
    const cell = value => '"' + String(value ?? '').replace(/^[=+@-]/, "'$&").replaceAll('"', '""') + '"';
    const rows = [columns, ...review.attempts.map(attempt => columns.map(column => attempt[column]))];
    const blob = new Blob(['\uFEFF' + rows.map(row => row.map(cell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob), link = document.createElement('a');
    link.href = url; link.download = `${review.id}-review.csv`; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  function renderArchive(tools) {
    $('archiveStatus').textContent = tools.archiveMessage || 'Choose Refresh list to find local recordings.';
    html('recordingList', (tools.recordings || []).map(recording => `<button class="recording-card ${recording.id === selectedRecording ? 'selected' : ''}" data-recording="${escapeHtml(recording.id)}" ${tools.archiveBusy ? 'disabled' : ''}><strong>${escapeHtml(recording.id)}</strong><span>${recording.active ? 'Recording now' : recording.finalized ? 'Finalized' : 'Incomplete / recovery required'}</span></button>`).join('') || '<p class="subtle">No recordings found.</p>');
    const review = tools.review, current = review && review.id === selectedRecording;
    $('downloadReview').disabled = !current || tools.archiveBusy;
    $('exportRecording').disabled = !current || !review.finalized || tools.archiveBusy;
    $('refreshRecordings').disabled = !!tools.archiveBusy;
    const signature = current ? JSON.stringify(review) : '';
    if (!current || signature === lastReview) return;
    lastReview = signature;
    html('recordingReview', `<h3>${escapeHtml(review.id)}</h3><p>${review.finalized ? 'Recording finalized' : 'Recording incomplete or still open'}</p><div class="blocks"><table class="runs-table"><thead><tr><th>Attempt</th><th>Status</th><th>Hits</th><th>Shots</th><th>Active time</th></tr></thead><tbody>${review.attempts.map(attempt => `<tr><td>${escapeHtml(attempt.blockId)}</td><td>${escapeHtml(attempt.status)}${attempt.reason ? `<small class="attempt-label">${escapeHtml(attempt.reason)}</small>` : ''}</td><td>${attempt.status === 'Skipped' ? '—' : attempt.hits}</td><td>${attempt.status === 'Skipped' ? '—' : attempt.shots}</td><td>${attempt.status === 'Skipped' ? '—' : formatTime(attempt.activeSeconds)}</td></tr>`).join('')}</tbody></table></div><h3>Saved notes</h3><ol class="note-timeline">${review.notes.map(note => `<li><span>${escapeHtml(readableNote(note.text))}</span></li>`).join('') || '<li>No operator notes.</li>'}</ol><p class="subtle">${escapeHtml(review.limitation)}</p>`);
  }

  function render(state) {
    if (!state?.tools) return;
    const tools = state.tools, calibration = tools.calibration || {};
    if (drag && !state.canStartParticipant) finishDrag(true);
    if (!drag && !setupBusy) {
      if (getOrder().join() !== state.conditionOrder.join()) setOrder(state.conditionOrder.slice());
      renderOrder();
    }
    $('setupWelcome').hidden = !state.canStartParticipant;
    $('preparationPanel').hidden = state.state !== 'Calibration';
    $('calibrationReview').textContent = calibration.review || 'Generate the synthetic fixture to review it.';
    ['Generate', 'Redo', 'Accept'].forEach(key => { const id = key.toLowerCase() + 'Calibration'; $(id).disabled = actionBusy || !calibration['can' + key]; });
    $('startPractice').disabled = actionBusy || !calibration.canPractice;
    $('practicePanel').hidden = !tools.canRestartPractice && state.state !== 'Practice';
    $('practiceTimer').textContent = state.state === 'Practice' ? formatTime(Math.ceil(state.remainingSeconds)) : 'Finished';
    $('restartPractice').disabled = actionBusy || !tools.canRestartPractice;
    $('finishPractice').disabled = actionBusy || !tools.canFinishPractice;
    const waitingForBreak = state.state === 'BlockEnded';
    $('breakPanel').hidden = !waitingForBreak && state.state !== 'Break';
    $('startBreak').hidden = !waitingForBreak;
    $('startBreak').disabled = actionBusy || !tools.canStartBreak;
    $('breakTimer').textContent = formatTime(Math.ceil(waitingForBreak ? tools.breakSeconds : state.remainingSeconds));
    $('breakHelp').textContent = state.busy ? 'Saving the test. Continue when saving finishes.' : waitingForBreak ? 'Test saved. Press Start break when you are ready to begin the countdown.' : tools.queueIndex + 1 >= tools.schedule.length ? 'The participant session closes after this break.' : 'When the timer ends, press Arm test when you are ready.';
    $('skipBreak').disabled = actionBusy || !tools.canSkipBreak;
    $('skipTest').disabled = actionBusy || !tools.canSkipTest;
    $('repeatTest').disabled = actionBusy || !tools.canRepeat || !tools.repeatable?.length;
    $('saveStatus').textContent = tools.saveStatus;
    const latestSave = tools.checkpoints?.at(-1);
    if (tools.saveStatus.startsWith('Saved ') && latestSave?.savedUtc) {
      const time = new Date(latestSave.savedUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
      $('saveStatus').textContent = `${latestSave.condition} saved at ${time} · ${latestSave.status}`;
    }
    $('saveStatus').classList.toggle('save-failed', /failed/i.test(tools.saveStatus));
    html('readinessList', tools.readiness.map(item => `<div class="readiness-item"><div><strong>${escapeHtml(item.name)}</strong><span class="readiness-${item.status.toLowerCase()}">${escapeHtml(item.status)}</span></div><p>${escapeHtml(item.detail)}</p></div>`).join(''));
    html('noteTimeline', tools.notes.map(note => `<li><time>${formatTime(note.seconds)}</time><span>${escapeHtml(readableNote(note.text))}</span></li>`).join(''));
    if (!phaseDirty) { $('practiceSeconds').value = tools.practiceSeconds; $('breakSeconds').value = tools.breakSeconds; }
    $('applyPhaseTiming').disabled = !!state.busy;
    $('loadPreset').disabled = setupBusy || !state.canStartParticipant;
    $('savePreset').disabled = setupBusy || !state.canStartParticipant;
    renderArchive(tools);
  }
  function offline() {
    finishDrag(true);
    $('conditionOrder').querySelectorAll('button').forEach(button => { button.disabled = true; });
    ['generateCalibration', 'redoCalibration', 'acceptCalibration', 'startPractice', 'restartPractice', 'finishPractice', 'startBreak', 'skipBreak', 'skipTest', 'repeatTest', 'applyPhaseTiming', 'loadPreset', 'savePreset', 'exportRecording', 'refreshRecordings']
      .forEach(id => { $(id).disabled = true; });
  }
  renderPresets();
  return { render, renderOrder, offline };
};
