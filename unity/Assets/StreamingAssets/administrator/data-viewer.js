/* The Data view is read-only with respect to test administration. Mode switches
 * leave the participant session, timer and acquisition running. */
window.createDataViewer = ({command, refresh, escapeHtml: esc, token}) => {
  const $ = id => document.getElementById(id);
  let clientError = '';
  let state, visible = false, selected = '', session = '', page = 0, stream = 'events.ndjson';
  let profileSignature = '', listSignature = '', rowsSignature = '', requestBusy = false;
  let taskId = '', detailTab = 'overview', detailPage = 0, outcome = 'All';
  let renderedReviewText, lastTaskRender, cachedReviewText, cachedReview;
  let chart = null, chartPosition = {key: '', index: 0};
  const number = (value, digits = 2) => typeof value === 'number' && Number.isFinite(value) ? value.toLocaleString(undefined, {maximumFractionDigits: digits}) : '—';
  const conditions = {WE_FT:'WE · Fixed targets', WE_MT:'WE · Moving targets', NE_FT:'NE · Fixed targets', NE_MT:'NE · Moving targets'};
  const summaryColumns = ['blockId','condition','status','activeSeconds','hits','shots','misses','hitsPerSecond','shotsPerSecond','missesPerSecond','accuracyPercent','meanShotErrorDeg','p95ShotErrorDeg','meanShotOffsetMm','meanMissOffsetMm','aimErrorVarianceDeg2','meanAimErrorDeg','meanAimSpeedDegPerSecond','aimSpeedVariabilityDegPerSecond','aimPathDeg','aimObservedSeconds','aimCoveragePercent','aimObservations','validAimObservations','shotGeometryCount','reason','metricsVersion','scoreStatus'];
  // The same saved review arrives with every live poll. Parse it only when its
  // source changes, and let task rendering use the stable parsed object identity.
  const reviewOf = record => {
    const text = record?.review || '{}';
    if (text !== cachedReviewText) {
      cachedReviewText = text;
      try {cachedReview = JSON.parse(text);} catch {cachedReview = {};}
    }
    return cachedReview;
  };
  const currentReview = () => reviewOf(state?.data?.profile?.sessions?.find(s => s.id === session));
  const date = value => value ? new Date(value).toLocaleString() : 'No saved sessions';
  const renderedHtml = new WeakMap();
  const html = (id, text) => {
    const element = $(id);
    // Compare our input, not the browser's serialized HTML. SVG closing tags,
    // entities and boolean attributes normalize on insertion; comparing innerHTML
    // replaced unchanged controls mid-press on every 10–30 Hz live update.
    if (renderedHtml.get(element) !== text) {
      element.innerHTML = text;
      renderedHtml.set(element, text);
    }
  };
  async function act(action, payload = {}) {
    requestBusy = true; clientError = ''; $('dataError').hidden = true; $('dataStatus').textContent = 'Working…';
    try { await command(action, payload); await refresh(); }
    catch (error) { clientError = error.message; $('dataError').textContent = clientError; $('dataError').hidden = false; }
    finally { requestBusy = false; }
  }
  $('recordingsButton').addEventListener('click', () => {
    visible = !visible; $('dataWorkspace').hidden = !visible; $('workspace').hidden = visible;
    $('recordingsButton').textContent = visible ? 'Test administration' : 'Data';
    $('recordingsButton').setAttribute('aria-pressed', String(visible));
    if (visible) { $('dataSearch').focus(); act('refreshData', {id:selected}); }
  });
  $('dataSearch').addEventListener('input', () => { listSignature = ''; render(state); });
  $('dataRefresh').addEventListener('click', () => { profileSignature = ''; act('refreshData', {id:selected}); });
  $('dataFolder').addEventListener('click', () => act('openDataFolder'));
  $('dataExportAll').addEventListener('click', () => act('exportDatabase'));
  $('dataProfiles').addEventListener('click', event => {
    const button = event.target.closest('[data-profile-id]'); if (!button || requestBusy) return;
    selected = button.dataset.profileId; profileSignature = ''; session = ''; taskId = ''; listSignature = '';
    html('dataProfile', '<p role="status">Loading participant…</p>');
    act('viewParticipant', {id: selected});
  });
  const loadRows = () => act('viewDataRows', {session, stream, page});
  $('dataProfile').addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button) return;
    if (button.dataset.taskId) {taskId = button.dataset.taskId; detailPage = 0; renderTasks(currentReview());}
    if (button.dataset.detailTab) {detailTab = button.dataset.detailTab; detailPage = 0; renderTasks(currentReview());}
    if (button.id === 'detailPrevious' || button.id === 'detailNext') {detailPage = Math.max(0, detailPage + (button.id === 'detailNext' ? 1 : -1)); renderTasks(currentReview());}
    if (button.id === 'dataExportWorkbook') act('exportWorkbook', {id:selected});
    if (button.id === 'dataReviewCsv') downloadReview();
    if (button.id === 'dataExportRaw') act('exportSessionRaw', {id:session});
    if (button.id === 'dataExportParticipant') act('exportParticipant', {id: selected});
    if (button.id === 'dataLoadRows') {page = 0; loadRows();}
    if (button.id === 'dataPrevious') {page = Math.max(0, page - 1); loadRows();}
    if (button.id === 'dataNext') {page++; loadRows();}
  });
  $('dataProfile').addEventListener('change', event => {
    if (event.target.id === 'dataSession') {session = event.target.value; taskId = ''; page = 0; detailPage = 0; profileSignature = ''; rowsSignature = ''; render(state);}
    if (event.target.id === 'shotOutcome') {outcome = event.target.value; detailPage = 0; renderTasks(currentReview());}
    if (event.target.id === 'dataStream') {stream = event.target.value; page = 0; loadRows();}
  });
  $('dataProfile').addEventListener('input', event => {
    if (event.target.id === 'chartTime') inspectChart(Number(event.target.value));
  });
  $('dataProfile').addEventListener('pointerdown', event => {
    const svg = event.target.closest('#ratePlot');
    if (!svg || event.button !== 0) return;
    svg.setPointerCapture(event.pointerId);
    inspectPointer(event, svg);
  });
  $('dataProfile').addEventListener('pointermove', event => {
    const svg = event.target.closest('#ratePlot');
    if (svg && (event.pointerType === 'mouse' || svg.hasPointerCapture(event.pointerId))) inspectPointer(event, svg);
  });
  $('dataProfile').addEventListener('pointerup', event => {
    const svg = event.target.closest('#ratePlot');
    if (svg?.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
  });
  $('dataProfile').addEventListener('wheel', event => {
    if (!chart || !event.target.closest('.rate-chart')) return;
    // Normal vertical scrolling still moves the page. Horizontal scrolling or
    // Shift+wheel steps through bins without creating a page-scroll trap.
    if (!event.shiftKey && Math.abs(event.deltaX) <= Math.abs(event.deltaY)) return;
    const delta = event.deltaX || event.deltaY;
    if (!delta) return;
    event.preventDefault();
    inspectChart(chartPosition.index + Math.sign(delta));
  }, {passive:false});
  function downloadReview() {
    const saved = state?.data?.profile?.sessions?.find(s => s.id === session);
    const attempts = reviewOf(saved).attempts || [];
    const columns = ['participantCode','sessionId', ...summaryColumns];
    const cell = value => '"' + String(value ?? '').replace(/^(\s*[=+@-])/, "'$1").replaceAll('"', '""') + '"';
    const rows = [columns, ...attempts.map(a => [state.data.profile.participant.code,session,...summaryColumns.map(c => a[c])])];
    const url = URL.createObjectURL(new Blob(['\uFEFF' + rows.map(r => r.map(cell).join(',')).join('\r\n')], {type:'text/csv;charset=utf-8'}));
    const link = document.createElement('a'); link.href = url; link.download = 'test-review.csv'; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  function noteText(note) {
    if (!note.text?.startsWith('Participant details: ')) return note.text;
    try { const details = JSON.parse(note.text.slice('Participant details: '.length)); return details.initialNotes ? `Initial observations: ${details.initialNotes}` : ''; }
    catch { return note.text; }
  }
  const metric = (label, value, unit, note = '') => `<div class="metric-card"><span>${esc(label)}</span><strong>${number(value)} <small>${esc(unit)}</small></strong>${note ? `<p>${esc(note)}</p>` : ''}</div>`;
  function rateChart(rows) {
    if (!rows.length) return '<p class="subtle">The timeline appears once active task time has been saved.</p>';
    const keys = ['hitsPerSecond','shotsPerSecond','missesPerSecond'];
    const colors = ['#167160','#426ca0','#b55737'];
    const maximum = Math.max(1, ...rows.flatMap(r => keys.map(k => r[k] || 0)));
    const duration = rows.at(-1).second + rows.at(-1).exposureSeconds;
    // The chart is a visual overview; the adjacent table retains every exact bin.
    const paths = keys.map((key, i) => {
      let pen = false;
      const d = rows.map(r => {
        if (typeof r[key] !== 'number') {pen = false; return '';}
        const command = pen ? 'L' : 'M'; pen = true;
        // Each rate belongs to a whole exposure bin; steps avoid implying an
        // interpolated rate between observations and show single-bin tasks too.
        return `${command}${40 + r.second * 720 / duration},${155 - r[key] / maximum * 130} H${40 + (r.second + r.exposureSeconds) * 720 / duration}`;
      }).join(' ');
      return `<path d="${d}" fill="none" stroke="${colors[i]}" stroke-width="2.5"/>`;
    }).join('');
    return `<div class="rate-chart"><div class="chart-legend"><span>● Hits/s</span><span>● Shots/s</span><span>● Misses/s</span></div><svg id="ratePlot" viewBox="0 0 800 190" preserveAspectRatio="none" role="img" aria-label="Firing rates across active task seconds. Inspect exact values using the time slider below."><path d="M40 20V155H770" stroke="#bac7bc" fill="none"/><text x="0" y="30">${number(maximum, 1)}</text><text x="15" y="157">0</text>${paths}<g class="chart-cursor" pointer-events="none"><rect id="chartBand" y="20" height="135"/><line id="chartCursor" y1="20" y2="155"/>${keys.map((key,i) => `<circle data-chart-dot="${key}" r="4" fill="${colors[i]}"/>`).join('')}</g><text x="40" y="182">0 s</text><text x="665" y="182">${number(duration)} active s</text></svg><div class="chart-scrubber"><label for="chartTime">Inspect active time</label><input id="chartTime" type="range" min="0" max="${rows.length - 1}" step="1" value="0" aria-describedby="chartHelp"><span id="chartInterval"></span></div><p id="chartHelp" class="subtle">Hover or drag across the graph. Use the slider, arrow keys, or Shift + scroll to inspect each interval.</p><div class="chart-readout" aria-label="Values in the selected time interval">${['hits','shots','misses'].map(key => `<div><span>${key[0].toUpperCase() + key.slice(1)} in interval</span><strong data-chart-count="${key}">—</strong><small data-chart-rate="${key}">— /s</small></div>`).join('')}<div><span>Mean aim error</span><strong id="chartAimError">—</strong><small>degrees</small></div></div></div>`;
  }
  function mountChart(rows) {
    const svg = $('ratePlot');
    chart = svg && rows.length ? {svg, rows,
      duration: rows.at(-1).second + rows.at(-1).exposureSeconds,
      maximum: Math.max(1, ...rows.flatMap(r => ['hitsPerSecond','shotsPerSecond','missesPerSecond'].map(key => r[key] || 0)))} : null;
    const key = JSON.stringify([selected, session, taskId]);
    if (key !== chartPosition.key) chartPosition = {key, index:0};
    if (chart) inspectChart(chartPosition.index);
  }
  function inspectPointer(event, svg) {
    if (!chart || chart.svg !== svg) return;
    const bounds = svg.getBoundingClientRect();
    const time = Math.max(0, Math.min(chart.duration, ((event.clientX - bounds.left) / bounds.width * 800 - 40) / 720 * chart.duration));
    // Search actual interval starts, rather than rounding the mouse position.
    // That keeps boundary selection correct for a fractional final second.
    let low = 0, high = chart.rows.length;
    while (low < high) {
      const middle = (low + high) >>> 1;
      if (chart.rows[middle].second <= time) low = middle + 1;
      else high = middle;
    }
    inspectChart(Math.max(0, low - 1));
  }
  function inspectChart(index) {
    if (!chart || !Number.isFinite(index)) return;
    index = Math.max(0, Math.min(chart.rows.length - 1, Math.round(index)));
    // Pointer moves update only a few attributes/text nodes, never the plot or
    // task controls. Polling therefore cannot reset a drag or keyboard position.
    if (index === chart.index) return;
    chart.index = chartPosition.index = index;
    const row = chart.rows[index];
    const start = 40 + row.second / chart.duration * 720;
    const width = row.exposureSeconds / chart.duration * 720;
    const x = start + width / 2;
    $('chartBand').setAttribute('x', start); $('chartBand').setAttribute('width', width);
    $('chartCursor').setAttribute('x1', x); $('chartCursor').setAttribute('x2', x);
    const interval = `${number(row.second, 3)}–${number(row.second + row.exposureSeconds, 3)} active s (${number(row.exposureSeconds, 3)} s interval)`;
    $('chartInterval').textContent = interval;
    $('chartTime').value = index;
    const accessibleValues = [];
    for (const key of ['hits','shots','misses']) {
      const rate = row[`${key}PerSecond`];
      document.querySelector(`[data-chart-count="${key}"]`).textContent = number(row[key], 0);
      document.querySelector(`[data-chart-rate="${key}"]`).textContent = `${number(rate, 3)} /s`;
      const dot = chart.svg.querySelector(`[data-chart-dot="${key}PerSecond"]`);
      dot.style.display = typeof rate === 'number' && Number.isFinite(rate) ? '' : 'none';
      if (typeof rate === 'number' && Number.isFinite(rate)) {
        dot.setAttribute('cx', x); dot.setAttribute('cy', 155 - rate / chart.maximum * 130);
      }
      accessibleValues.push(`${key}: ${number(row[key], 0)}, ${number(rate, 3)} per second`);
    }
    $('chartAimError').textContent = number(row.meanAimErrorDeg, 3);
    $('chartTime').setAttribute('aria-valuetext', `${interval}; ${accessibleValues.join('; ')}`);
  }
  function detailTable(rows, columns) {
    const size = 25, pages = Math.max(1, Math.ceil(rows.length / size));
    detailPage = Math.min(detailPage, pages - 1);
    const displayed = rows.slice(detailPage * size, (detailPage + 1) * size);
    return `<div class="data-table"><table class="runs-table"><thead><tr>${columns.map(([,label]) => `<th scope="col">${esc(label)}</th>`).join('')}</tr></thead><tbody>${displayed.map(row => `<tr>${columns.map(([key]) => `<td>${typeof row[key] === 'number' ? number(row[key], 3) : esc(row[key] ?? '—')}</td>`).join('')}</tr>`).join('') || `<tr><td colspan="${columns.length}">No matching observations.</td></tr>`}</tbody></table></div><div class="data-actions detail-paging"><button id="detailPrevious" class="ghost-btn" ${detailPage === 0 ? 'disabled' : ''}>Previous</button><span>${rows.length} rows · Page ${detailPage + 1} of ${pages}</span><button id="detailNext" class="ghost-btn" ${detailPage + 1 >= pages ? 'disabled' : ''}>Next</button></div>`;
  }
  function renderTasks(review) {
    const element = $('taskExplorer');
    if (!element) return;
    const unchanged = lastTaskRender && lastTaskRender.element === element && lastTaskRender.review === review &&
      lastTaskRender.taskId === taskId && lastTaskRender.tab === detailTab && lastTaskRender.page === detailPage && lastTaskRender.outcome === outcome;
    if (unchanged) return;
    const attempts = review.attempts || [];
    if (!attempts.some(a => a.blockId === taskId)) taskId = attempts[0]?.blockId || '';
    const a = attempts.find(a => a.blockId === taskId);
    if (!a) {
      chart = null;
      html('taskExplorer', '<div class="data-empty"><h3>No saved tasks yet</h3><p>Finish a task and refresh to see its results here.</p></div>');
      lastTaskRender = {element, review, taskId, tab:detailTab, page:detailPage, outcome};
      return;
    }
    const taskButtons = attempts.map((task, i) => `<button type="button" class="task-choice" data-task-id="${esc(task.blockId)}"><small>ATTEMPT ${i + 1}</small><strong>${esc(conditions[task.condition] || task.condition)}</strong><span>${esc(task.status)} · ${number(task.activeSeconds)} s</span></button>`).join('');
    const tabs = [['overview','Overview'],['timeline','Timeline'],['shots','Shots'],['guide','Metric guide']].map(([key,label]) => `<button type="button" data-detail-tab="${key}">${label}</button>`).join('');
    let body = '';
    if (detailTab === 'overview') body = `<div class="metric-grid rates">${metric('Hits per second',a.hitsPerSecond,'hits/s',`${number(a.hits,0)} hit shots`)}${metric('Shots per second',a.shotsPerSecond,'shots/s',`${number(a.shots,0)} accepted shots`)}${metric('Misses per second',a.missesPerSecond,'misses/s',`${number(a.misses,0)} missed shots`)}${metric('Accuracy',a.accuracyPercent,'%', 'Hit shots ÷ accepted shots')}</div>${rateChart(a.seconds || [])}<h4>Shot precision</h4><div class="metric-grid">${metric('Mean shot error',a.meanShotErrorDeg,'°','Angle from target center')}${metric('95th percentile error',a.p95ShotErrorDeg,'°','95% of measured shot errors at or below')}${metric('Mean miss distance',a.meanMissOffsetMm,'mm','Target center to bore ray · misses only')}</div><h4>Aim stability & movement</h4><div class="metric-grid">${metric('Aim error variance',a.aimErrorVarianceDeg2,'deg²','Variation in target angular error')}${metric('Mean aim speed',a.meanAimSpeedDegPerSecond,'°/s','Angular travel over observed time')}${metric('Aim speed variability',a.aimSpeedVariabilityDegPerSecond,'°/s','Variation in motion speed')}</div><div class="coverage-strip"><strong>Measurement coverage</strong><span>${number(a.shotGeometryCount,0)} / ${number(a.shots,0)} shots with geometry</span><span>${number(a.aimCoveragePercent)}% aim motion coverage</span><span>${number(a.validAimObservations,0)} / ${number(a.aimObservations,0)} valid aim observations</span></div>`;
    if (detailTab === 'timeline') body = `<p class="subtle">One row per active second, including seconds with no shots. Pauses are excluded; a partial final second uses its actual duration.</p>${rateChart(a.seconds || [])}${detailTable(a.seconds || [], [['second','Active second'],['exposureSeconds','Duration (s)'],['hitsPerSecond','Hits/s'],['shotsPerSecond','Shots/s'],['missesPerSecond','Misses/s'],['meanAimErrorDeg','Aim error (°)']])}`;
    if (detailTab === 'shots') body = `<label for="shotOutcome">Shot outcome</label><select id="shotOutcome">${['All','Hit','Miss','Unknown'].map(value => `<option ${outcome === value ? 'selected' : ''}>${value}</option>`).join('')}</select><p class="subtle">Error is relative to the hit target, or the nearest visible forward target for a miss. This reference does not establish intended target.</p>${detailTable((a.shotDetails || []).filter(s => outcome === 'All' || s.outcome === outcome), [['shotNumber','Shot'],['activeSeconds','Active time (s)'],['outcome','Outcome'],['angularErrorDeg','Error (°)'],['centerOffsetMm','Center offset (mm)'],['edgeClearanceMm','Edge clearance (mm)'],['referenceTargetId','Reference target']])}`;
    if (detailTab === 'guide') body = `<dl class="metric-guide"><dt>Rates & accuracy</dt><dd>Rates divide accepted shots, hit shots or misses by active task seconds. Accuracy is hit shots ÷ shots. Trigger lockout and invalid tracking are excluded.</dd><dt>Shot error & distance</dt><dd>Angular error measures the angle to the target center. Center offset is the perpendicular distance from the center to the forward bore ray, in millimetres. Edge clearance subtracts target radius, with a minimum of zero.</dd><dt>Aim variance</dt><dd>Sample variance (n−1) of target angular error, in squared degrees. Includes target switching. At least two valid observations with a reference target are needed.</dd><dt>Erratic aim</dt><dd>Aim speed variability is the duration-weighted standard deviation of angular speed. Together with mean speed and variance, it describes movement without assigning an arbitrary good/bad score. Deliberate target transitions can also increase it.</dd><dt>Coverage & gaps</dt><dd>Aim telemetry uses a configured frame cadence. Motion metrics never bridge pauses, invalid observations or excessive gaps. Low coverage limits interpretation; full raw tracking remains available.</dd><dt>Unavailable values</dt><dd>A dash means unavailable, including older recordings without geometry, tasks with no shots, skipped tasks and insufficient observations. Excel leaves these cells blank and SQL uses NULL.</dd></dl>`;
    // Keep navigation nodes in place even when a local selection changes. This
    // preserves keyboard focus and makes repeated/held presses reliable.
    html('taskExplorer', '<div id="taskChoices" class="task-choices" aria-label="Choose a task attempt"></div><div id="taskHeading"></div><nav id="detailTabs" class="detail-tabs" aria-label="Task data sections"></nav><div id="taskDetailBody" class="task-detail-body"></div>');
    html('taskChoices', taskButtons);
    html('detailTabs', tabs);
    for (const button of element.querySelectorAll('[data-task-id], [data-detail-tab]')) {
      const active = button.dataset.taskId ? button.dataset.taskId === taskId : button.dataset.detailTab === detailTab;
      button.classList.toggle('selected', active);
      button.setAttribute('aria-pressed', String(active));
    }
    html('taskHeading', `<div class="task-detail-heading"><div><span class="eyebrow">SELECTED TASK</span><h3>${esc(conditions[a.condition] || a.condition)}</h3><small>${esc(a.blockId)}</small></div><span class="task-status">${esc(a.status)} · ${number(a.activeSeconds)} active s</span></div>${a.reason ? `<p class="task-reason">${esc(a.reason)}</p>` : ''}${!a.metricsVersion ? '<p class="task-notice">Legacy review. Refresh to calculate available rates; aim geometry requires a new recording.</p>' : ''}`);
    html('taskDetailBody', body);
    mountChart(a.seconds || []);
    lastTaskRender = {element, review, taskId, tab:detailTab, page:detailPage, outcome};
  }
  function render(current) {
    if (!current) return; state = current;
    const data = state.data;
    if (!data) {
      $('dataStatus').textContent = 'This player does not provide the Data service. Close ELTS and run START-ELTS to rebuild the updated player.';
      return;
    }
    $('dataStatus').textContent = data.message || 'Preparing storage…';
    $('dataPath').textContent = data.path || '';
    $('dataError').textContent = clientError || data.error || ''; $('dataError').hidden = !clientError && !data.error;
    $('dataLive').textContent = state.participant && !state.canStartParticipant ? `Participant ${state.participant} · ${state.phase || state.state}. Test administration remains active while you view data.` : 'Viewing saved local data';
    $('dataDownload').hidden = !state.downloadId || data.busy;
    if (!visible) $('recordingsButton').textContent = data.error ? 'Data !' : 'Data';
    $('recordingsButton').title = data.error || 'Browse participant data';
    $('dataDownload').href = state.downloadId ? `/api/download/${encodeURIComponent(state.downloadId)}?token=${encodeURIComponent(token)}` : '#';
    $('dataDownload').setAttribute('download', '');
    // Administration keeps its live updates; saved-data markup does no hidden
    // work while the participant view is on screen.
    if (!visible) return;
    ['dataRefresh', 'dataExportAll'].forEach(id => $(id).disabled = data.busy || requestBusy);
    const profiles = (data.profiles || []).filter(p => `${p.code} ${p.name}`.toLowerCase().includes($('dataSearch').value.toLowerCase()));
    const list = JSON.stringify([profiles, selected, data.busy]);
    if (list !== listSignature) {
      listSignature = list;
      html('dataProfiles', profiles.map(p => `<button class="data-person ${p.id === selected ? 'selected' : ''}" data-profile-id="${esc(p.id)}" ${data.busy ? 'disabled' : ''}><strong>${esc(p.code)}</strong><span>${esc(p.name || 'Name not supplied')}</span><small>${esc(p.sessions)} sessions · ${esc(date(p.modifiedUtc))}</small></button>`).join('') || '<p class="subtle">No matching participants. Refresh to index existing recordings.</p>');
    }
    const profile = data.profile;
    if (!profile?.participant || profile.participant.id !== selected) return;
    const sessions = profile.sessions || [];
    if (!sessions.some(s => s.id === session)) session = sessions[0]?.id || '';
    const record = sessions.find(s => s.id === session);
    const reviewText = record?.review || '{}';
    // Large reviews from other sessions do not belong in a high-frequency DOM
    // signature. Only navigation metadata and the selected review can change it.
    const signature = JSON.stringify([profile.participant, sessions.map(s => [s.id, s.modifiedUtc, s.finalized]), session]);
    if (signature !== profileSignature || reviewText !== renderedReviewText) {
      profileSignature = signature; rowsSignature = '';
      renderedReviewText = reviewText;
      const review = reviewOf(record);
      html('dataProfile', `<div class="data-heading"><div><span class="eyebrow">PARTICIPANT PROFILE</span><h2>${esc(profile.participant.code)}</h2><p>${esc(profile.participant.name || 'Name not supplied')}</p></div><div class="data-actions"><button id="dataExportWorkbook" class="primary-btn">Export Excel</button><button id="dataExportParticipant" class="ghost-btn">Export participant SQL</button></div></div><label for="dataSession">Saved session</label><select id="dataSession">${sessions.map(s => `<option value="${esc(s.id)}" ${s.id === session ? 'selected' : ''}>${esc(date(s.modifiedUtc))} · ${s.finalized === '1' ? 'Finalized' : 'Open / incomplete'}</option>`).join('')}</select><div class="data-actions"><button id="dataExportRaw" class="ghost-btn" ${record?.finalized === '1' ? '' : 'disabled'}>Export original files</button><button id="dataReviewCsv" class="ghost-btn">Download review CSV</button></div><p class="subtle">Descriptive synthetic observations · pauses excluded · — means unavailable.</p><section id="taskExplorer" aria-label="Task results"></section><h3>Session notes</h3><ul class="data-notes">${(review.notes || []).map(noteText).filter(Boolean).map(text => `<li>${esc(text)}</li>`).join('') || '<li>No session notes.</li>'}</ul><details><summary>Explore original records</summary><p>SQL copy of complete saved records. Refresh after a test finishes to see its checkpoint.</p><label for="dataStream">Record stream</label><select id="dataStream">${['events.ndjson','samples.ndjson','targets.ndjson','session-summary.json'].map(s => `<option ${s === stream ? 'selected' : ''}>${esc(s)}</option>`).join('')}</select><button id="dataLoadRows" class="ghost-btn">Load records</button><pre id="dataRawRows">Choose Load records.</pre><div class="data-actions"><button id="dataPrevious" class="ghost-btn" disabled>Previous</button><span id="dataPage">Page 1</span><button id="dataNext" class="ghost-btn" disabled>Next</button></div></details>`);
    }
    renderTasks(currentReview());
    $('dataExportWorkbook').disabled = data.busy || requestBusy;
    $('dataExportParticipant').disabled = data.busy || requestBusy;
    $('dataExportRaw').disabled = data.busy || requestBusy || sessions.find(s => s.id === session)?.finalized !== '1';
    $('dataLoadRows').disabled = data.busy || requestBusy;
    const matchingRows = data.rowsSession === undefined || (data.rowsSession === session && data.rowsStream === stream && data.rowsPage === page);
    const loadedRows = matchingRows ? data.rows || [] : [];
    const rows = JSON.stringify([loadedRows, page, data.busy]);
    if (rows !== rowsSignature && $('dataRawRows')) {
      rowsSignature = rows;
      $('dataRawRows').textContent = loadedRows.map(row => {try {return JSON.stringify(JSON.parse(row.json), null, 2);} catch {return row.json;}}).join('\n\n') || 'No loaded records on this page.';
      $('dataPrevious').disabled = data.busy || page === 0;
      $('dataNext').disabled = data.busy || loadedRows.length < 50;
      $('dataPage').textContent = `Page ${page + 1}`;
    }
  }
  return {render};
};
