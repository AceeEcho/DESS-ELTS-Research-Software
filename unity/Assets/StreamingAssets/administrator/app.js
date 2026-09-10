/* ELTS administrator console. The server remains the source of truth.
 * This page only renders state and sends explicit operator commands. */
(() => {
  const $ = (id) => document.getElementById(id);
  const conditions = ['WE_FT', 'WE_MT', 'NE_FT', 'NE_MT'];
  const token = decodeURIComponent(location.hash.replace(/^#/, ''));
  const savedPane = Number(localStorage.getItem('elts-admin-pane'));
  let state = null, order = conditions.slice(), pollBusy = false, viewVersion = null;
  let orbit = { yaw: 0, pitch: 0, distance: 1 }, dragging = false, lastPoint = null;
  const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const command = async (action, payload = {}) => {
    const response = await fetch('/api/command', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Elts-Token': token }, body: JSON.stringify({ action, runId: state?.runId || '', ...payload }) });
  const result = await response.json();
  if (!result.ok) throw new Error(result.message || 'Command was rejected.');
  return result;
  };
  const notifyError = (element, error) => { element.textContent = error.message || String(error);
  element.hidden = false; };
  const formatTime = (seconds) => { const total = Math.max(0, Math.floor(Number(seconds) || 0));
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`; };
  const titleCase = (value) => String(value || '').replaceAll('_', ' ');
  function setConnection(online) {
    $('connectionDot').classList.toggle('offline', !online);
  $('connectionText').textContent = online ? 'Connected' : 'Offline';
  }
  function renderBlocks() {
    const blocks = state?.blocks || [];
  const current = state?.condition;
  const rows = (state?.conditionOrder?.length ? state.conditionOrder : order).map((condition, index) => {
      const block = blocks.find((candidate) => candidate.condition === condition);
  const status = block?.status || (condition === current ? 'Current' : 'Queued');
      const statusText = block?.status || (condition === current ? 'Current' : '—');
      return `<tr class="${condition === current ? 'current' : ''}"><td>${escapeHtml(condition)}</td><td class="${/complete|done/i.test(statusText) ? 'done' : ''}">${escapeHtml(titleCase(statusText))}</td><td>${block?.hits ?? '—'}</td><td>${block?.shots ?? '—'}</td></tr>`;
    }).join('');
    $('blocks').innerHTML = rows || '<tr><td colspan="4" class="subtle">No conditions loaded</td></tr>';
    const planned = state?.conditionOrder?.length || order.length; const completed = (state?.conditionOrder || order).filter((condition) => /complete|done/i.test(blocks.find((block) => block.condition === condition)?.status || '')).length;
    $('blockSummary').textContent = planned ? `${completed}/${planned} complete` : '—';
  }
  function render() {
    if (!state) return;
  const phase = state.phase || state.state || 'idle';
  const running = /running|armed|ready|waiting/i.test(`${phase} ${state.state || ''}`);
  $('participant').textContent = state.participant || '—';
  $('runId').textContent = state.runId ? `Run ${state.runId}` : 'No run';
  $('phaseText').textContent = titleCase(phase);
  $('condition').textContent = state.condition || 'No condition selected';
  $('remaining').textContent = state.remainingSeconds == null ? '—' : formatTime(state.remainingSeconds);
  $('elapsed').textContent = formatTime(state.elapsedSeconds);
  $('shots').textContent = state.shots ?? 0;
  $('hits').textContent = state.hits ?? 0;
  $('accuracy').textContent = state.shots ? `${Math.round((Number(state.hits || 0) / Number(state.shots)) * 100)}%` : '—';
  const badge = $('stateBadge');
  badge.textContent = String(state.state || phase).toUpperCase();
  badge.className = `badge ${state.armed ? 'armed' : running ? 'running' : /abort|error/i.test(phase) ? 'error' : ''}`;
  const armText = state.armed ? 'ARMED · waiting for first shot' : state.canArm ? 'Ready to arm this condition' : state.message || (state.participant ? 'Preparing participant pipeline…' : 'Waiting for participant');
  $('armStatus').innerHTML = `<span class="status-dot ${state.armed ? 'warn' : state.canArm ? '' : 'offline'}"></span><span>${escapeHtml(armText)}</span>`;
  $('armButton').disabled = !state.canArm && !state.armed;
  $('armButton').textContent = state.armed ? 'DISARM TEST' : 'ARM TEST';
  $('armButton').classList.toggle('armed', !!state.armed);
  $('disarmButton').disabled = !state.armed;
  $('nextButton').disabled = state.canNext !== true;
  $('abortButton').disabled = !state.participant || !!state.busy || !running;
  $('recordingPath').textContent = state.recordingPath || 'Recording path unavailable';
  $('startParticipant').disabled = state.canStartParticipant !== true;
  renderBlocks();
  renderTracking();
  renderImage();
  }
  function renderTracking() {
    const tracking = state?.tracking || {};
  $('headTracking').textContent = tracking.head?.valid ? 'Valid signal' : 'No signal';
  $('weaponTracking').textContent = tracking.weapon?.valid ? 'Valid signal' : 'No signal';
  if (!dragging) { const camera = state?.camera || orbit;
  orbit = { yaw: Number(camera.yaw || 0), pitch: Math.max(-10, Math.min(80, Number(camera.pitch || 0))), distance: Math.max(1.5, Math.min(12, Number(camera.distance || 4))) }; }
    $('cameraInfo').textContent = `${Math.round(orbit.yaw)}° · ${Math.round(orbit.pitch)}° · ${orbit.distance.toFixed(1)} m`;
  }
  function renderImage() {
    const version = state?.viewVersion;
  if (!version || version === viewVersion) return;
  viewVersion = version;
  const image = $('viewImage');
  image.src = `/api/view.jpg?token=${encodeURIComponent(token)}&v=${encodeURIComponent(version)}`;
  image.hidden = false;
  $('viewPlaceholder').hidden = true;
  $('viewVersion').textContent = `Frame ${version}`;
  }
  async function refresh() {
    if (pollBusy) return;
  pollBusy = true;
  try { const response = await fetch(`/api/state${token ? `?token=${encodeURIComponent(token)}` : ''}`, { headers: { 'X-Elts-Token': token }, cache: 'no-store' });
  if (!response.ok) throw new Error(`State request failed (${response.status}).`);
  state = await response.json();
  setConnection(true);
  render(); }
    catch (error) { setConnection(false);
  $('phaseText').textContent = 'Unavailable'; }
    finally { pollBusy = false; }
  }
  function renderOrder() { $('conditionOrder').innerHTML = order.map((condition, index) => `<li class="order-item"><span class="order-num">${index + 1}</span><strong>${escapeHtml(condition)}</strong><button type="button" data-move="up" data-index="${index}" aria-label="Move ${condition} up">↑</button><button type="button" data-move="down" data-index="${index}" aria-label="Move ${condition} down">↓</button></li>`).join(''); }
  function openDialog() { order = (state?.conditionOrder?.length ? state.conditionOrder : conditions).slice();
  renderOrder();
  $('participantInput').value = state?.participant || '';
  $('startError').hidden = true;
  $('startDialog').hidden = false;
  $('participantInput').focus(); }
  function closeDialog() { $('startDialog').hidden = true; }
  function openAbort() { $('abortReason').value = '';
  $('abortError').hidden = true;
  $('abortDialog').hidden = false;
  $('abortReason').focus(); }
  function closeAbort() { $('abortDialog').hidden = true; }
  function wire() {
    $('startParticipant').addEventListener('click', openDialog);
  $('closeDialog').addEventListener('click', closeDialog);
  $('cancelDialog').addEventListener('click', closeDialog);
  $('conditionOrder').addEventListener('click', (event) => { const button = event.target.closest('button');
  if (!button) return;
  const from = Number(button.dataset.index);
  const to = button.dataset.move === 'up' ? from - 1 : from + 1;
  if (to < 0 || to >= order.length) return; [order[from], order[to]] = [order[to], order[from]];
  renderOrder(); });
  $('startForm').addEventListener('submit', async (event) => { event.preventDefault();
  const participant = $('participantInput').value.trim();
  if (!participant) { $('startError').textContent = 'Participant ID is required.';
  $('startError').hidden = false;
  return; } try { await command('startParticipant', { participant, conditionOrder: order });
  closeDialog();
  await refresh(); } catch (error) { notifyError($('startError'), error); } });
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
  $('noteText').value = ''; } catch (error) { $('noteText').setCustomValidity(error.message);
  $('noteText').reportValidity(); } });
  $('windowed').addEventListener('click', () => command('windowed').catch((error) => { $('viewVersion').textContent = error.message; }));
  $('fullscreen').addEventListener('click', () => command('fullscreen').catch((error) => { $('viewVersion').textContent = error.message; }));
  let orbitTimer = null;
  const sendOrbit = () => command('viewOrbit', orbit).catch((error) => { $('viewVersion').textContent = error.message; });
  const viewport = $('viewport');
  viewport.addEventListener('pointerdown', (event) => { dragging = true;
  lastPoint = event;
  viewport.setPointerCapture(event.pointerId); });
  viewport.addEventListener('pointermove', (event) => { if (!dragging) return;
  orbit.yaw += (event.clientX - lastPoint.clientX) * .35;
  orbit.pitch = Math.max(-10, Math.min(80, orbit.pitch + (event.clientY - lastPoint.clientY) * .25));
  lastPoint = event;
  renderTracking();
  if (!orbitTimer) orbitTimer = setTimeout(() => { orbitTimer = null;
  sendOrbit(); }, 80); });
  viewport.addEventListener('pointerup', () => { if (!dragging) return;
  dragging = false;
  if (orbitTimer) { clearTimeout(orbitTimer);
  orbitTimer = null; } sendOrbit(); });
  viewport.addEventListener('wheel', (event) => { event.preventDefault();
  orbit.distance = Math.max(1.5, Math.min(12, orbit.distance + event.deltaY * .01));
  renderTracking();
  sendOrbit(); }, { passive: false });
  $('splitHandle').addEventListener('pointerdown', (event) => { const workspace = $('workspace');
  const move = (e) => { const ratio = Math.max(.3, Math.min(.65, (workspace.getBoundingClientRect().right - e.clientX) / workspace.clientWidth));
  document.documentElement.style.setProperty('--pane', `${ratio * 100}%`);
  localStorage.setItem('elts-admin-pane', ratio); };
  const done = () => { window.removeEventListener('pointermove', move);
  window.removeEventListener('pointerup', done); };
  window.addEventListener('pointermove', move);
  window.addEventListener('pointerup', done); });
  $('splitHandle').addEventListener('keydown', (event) => { if (!['ArrowLeft','ArrowRight'].includes(event.key)) return;
  event.preventDefault();
  const current = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--pane')) / 100;
  const next = Math.max(.3, Math.min(.65, current + (event.key === 'ArrowLeft' ? -.02 : .02)));
  document.documentElement.style.setProperty('--pane', `${next * 100}%`);
  localStorage.setItem('elts-admin-pane', next); });
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape') { closeDialog();
  closeAbort(); } });
  }
  if (savedPane >= .3 && savedPane <= .65) document.documentElement.style.setProperty('--pane', `${savedPane * 100}%`);
  wire();
  refresh();
  setInterval(refresh, 300);
})();
