/* The Data view is read-only with respect to test administration. Mode switches
 * leave the participant session, timer and acquisition running. */
window.createDataViewer = ({command, refresh, escapeHtml: esc, token}) => {
  const $ = id => document.getElementById(id);
  let clientError = '';
  let state, visible = false, selected = '', session = '', page = 0, stream = 'events.ndjson';
  let profileSignature = '', listSignature = '', rowsSignature = '', requestBusy = false;
  const date = value => value ? new Date(value).toLocaleString() : 'No saved sessions';
  const html = (id, text) => { if ($(id).innerHTML !== text) $(id).innerHTML = text; };
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
    selected = button.dataset.profileId; profileSignature = ''; session = ''; listSignature = '';
    act('viewParticipant', {id: selected});
  });
  const loadRows = () => act('viewDataRows', {session, stream, page});
  $('dataProfile').addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button) return;
    if (button.id === 'dataReviewCsv') downloadReview();
    if (button.id === 'dataExportRaw') act('exportSessionRaw', {id:session});
    if (button.id === 'dataExportParticipant') act('exportParticipant', {id: selected});
    if (button.id === 'dataLoadRows') {page = 0; loadRows();}
    if (button.id === 'dataPrevious') {page = Math.max(0, page - 1); loadRows();}
    if (button.id === 'dataNext') {page++; loadRows();}
  });
  $('dataProfile').addEventListener('change', event => {
    if (event.target.id === 'dataSession') {session = event.target.value; profileSignature = ''; rowsSignature = ''; render(state);}
    if (event.target.id === 'dataStream') {stream = event.target.value; page = 0; loadRows();}
  });
  function downloadReview() {
    const saved = state?.data?.profile?.sessions?.find(s => s.id === session);
    const attempts = JSON.parse(saved?.review || '{}').attempts || [];
    const columns = ['condition','blockId','status','hits','shots','activeSeconds','reason','scoreStatus'];
    const cell = value => '"' + String(value ?? '').replace(/^[=+@-]/, "'$&").replaceAll('"', '""') + '"';
    const rows = [columns, ...attempts.map(a => columns.map(c => a[c]))];
    const url = URL.createObjectURL(new Blob(['\uFEFF' + rows.map(r => r.map(cell).join(',')).join('\r\n')], {type:'text/csv;charset=utf-8'}));
    const link = document.createElement('a'); link.href = url; link.download = 'test-review.csv'; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  function noteText(note) {
    if (!note.text?.startsWith('Participant details: ')) return note.text;
    try { const details = JSON.parse(note.text.slice('Participant details: '.length)); return details.initialNotes ? `Initial observations: ${details.initialNotes}` : ''; }
    catch { return note.text; }
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
    const signature = JSON.stringify([profile, session]);
    if (signature !== profileSignature) {
      profileSignature = signature; rowsSignature = '';
      const record = sessions.find(s => s.id === session);
      let review = {}; try { review = JSON.parse(record?.review || '{}'); } catch { /* Retain a usable profile if its descriptive review is unavailable. */ }
      html('dataProfile', `<div class="data-heading"><div><span class="eyebrow">PARTICIPANT PROFILE</span><h2>${esc(profile.participant.code)}</h2><p>${esc(profile.participant.name || 'Name not supplied')}</p></div><button id="dataExportParticipant" class="primary-btn">Export participant</button></div><p class="subtle">Unique profile: ${esc(profile.participant.id)}</p><label for="dataSession">Saved session</label><select id="dataSession">${sessions.map(s => `<option value="${esc(s.id)}" ${s.id === session ? 'selected' : ''}>${esc(date(s.modifiedUtc))} · ${s.finalized === '1' ? 'Finalized' : 'Open / incomplete'}</option>`).join('')}</select><div class="data-actions"><button id="dataExportRaw" class="ghost-btn" ${record?.finalized === '1' ? '' : 'disabled'}>Export original files</button><button id="dataReviewCsv" class="ghost-btn">Download review CSV</button></div><h3>Test results</h3><p class="subtle">Descriptive synthetic counts. Scores are unavailable; these results do not validate physical equipment.</p><div class="data-table"><table class="runs-table"><thead><tr><th>Test</th><th>Status</th><th>Hits</th><th>Shots</th><th>Active seconds</th></tr></thead><tbody>${(review.attempts || []).map(a => `<tr><td>${esc(a.condition)}<small>${esc(a.blockId)}</small></td><td>${esc(a.status)}</td><td>${esc(a.hits)}</td><td>${esc(a.shots)}</td><td>${esc(a.activeSeconds ?? '—')}</td></tr>`).join('') || '<tr><td colspan="5">No finished tests in this saved session.</td></tr>'}</tbody></table></div><h3>Session notes</h3><ul class="data-notes">${(review.notes || []).map(noteText).filter(Boolean).map(text => `<li>${esc(text)}</li>`).join('') || '<li>No session notes.</li>'}</ul><details><summary>Explore original records</summary><p>SQL copy of complete saved records. Refresh after a test finishes to see its checkpoint.</p><label for="dataStream">Record stream</label><select id="dataStream">${['events.ndjson','samples.ndjson','targets.ndjson','session-summary.json'].map(s => `<option ${s === stream ? 'selected' : ''}>${esc(s)}</option>`).join('')}</select><button id="dataLoadRows" class="ghost-btn">Load records</button><pre id="dataRawRows">Choose Load records.</pre><div class="data-actions"><button id="dataPrevious" class="ghost-btn" disabled>Previous</button><span id="dataPage">Page 1</span><button id="dataNext" class="ghost-btn" disabled>Next</button></div></details>`);
    }
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
