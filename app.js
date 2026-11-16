/* Parking Tracker — screen and interactions. All data and decisions come from live.js (window.PT). */
(() => {
  'use strict';
  const PT = window.PT;
  const { esc, fmtDate, todayISO } = PT;

  const EXPIRY_WARN_DAYS = 30;
  const MAX_FIX_AGE_MS = 15000;
  const RECHECK_MOVE_M = 8;
  const RECHECK_EVERY_MS = 30000;
  const LAYER_MIN_ZOOM = 16;
  const KIND_STYLE = {
    covered: { color: '#16a34a', label: 'free with your card' },
    mixed: { color: '#f59e0b', label: 'partly (check the number)' },
    pay: { color: '#dc2626', label: 'pay' },
    red: { color: '#7f1d1d', label: 'red zone (card never valid)' },
    disc: { color: '#2563eb', label: 'blue zone (parking disc)' },
    free: { color: '#0d9488', label: 'free for everyone' },
    check: { color: '#9ca3af', label: 'unknown, check signs' },
    unknown: { color: '#9ca3af', label: 'unknown, check signs' },
  };

  // ---------- small helpers ----------
  const $ = (id) => document.getElementById(id);
  const store = {
    get(k) { try { return JSON.parse(localStorage.getItem(k)); } catch { return null; } },
    set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); return true; } catch { return false; } },
  };
  const daysBetween = (a, b) => Math.round((new Date(b) - new Date(a)) / 86400000);
  const clock = (t = new Date()) => t.toLocaleTimeString('fr-BE', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

  const state = {
    card: store.get('pt.card'), sector: null, list: null,
    tracking: false, watchId: null, me: null, pin: null, lastStatus: null,
    lastCheck: null, checkSeq: 0, layerSeq: 0, spotSeq: 0, lastSpotAt: null,
  };

  // Everything a decision needs; the official-list check is refreshed when needed.
  let listPromise = null;
  async function ctx() {
    const stale = !state.list || (state.list.status !== 'ok' && Date.now() - state.list.t > 60000)
      || (state.list.status === 'ok' && Date.now() - state.list.t > PT.C.LIST_RECHECK_MS);
    if (stale && state.sector) {
      listPromise = listPromise || PT.checkOfficialList(state.sector).then((l) => { state.list = l; listPromise = null; return l; });
      await listPromise;
    }
    return { card: state.card, sector: state.sector, codes: new Set(state.card?.tolerances || []), listProblem: PT.listProblem(state.list) };
  }

  // ---------- map ----------
  const map = L.map('map', { zoomControl: false, preferCanvas: true }).setView([50.8285, 4.3640], 16);
  L.control.zoom({ position: 'topleft' }).addTo(map);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> · zones: parking.brussels (live)',
  }).addTo(map);
  const surfacesLayer = L.layerGroup().addTo(map);
  let meMarker = null, meAcc = null, pinMarker = null, targetMarker = null;

  const legend = L.control({ position: 'topright' });
  legend.onAdd = () => {
    const d = L.DomUtil.create('div', 'legend');
    d.innerHTML = `<div class="legend-title">Live street colours</div>` + ['covered', 'mixed', 'pay', 'red', 'disc', 'free', 'unknown']
      .map((k) => `<div><i style="background:${KIND_STYLE[k].color}"></i>${KIND_STYLE[k].label}</div>`).join('')
      + '<div id="layerState" class="legend-state"></div>';
    L.DomEvent.disableClickPropagation(d);
    d.onclick = () => d.classList.toggle('open');
    return d;
  };
  legend.addTo(map);

  // Live street colours for what is on screen.
  let layerTimer = null;
  function scheduleLayer() { clearTimeout(layerTimer); layerTimer = setTimeout(refreshLayer, 700); }
  async function refreshLayer() {
    const seq = ++state.layerSeq, st = $('layerState');
    if (!state.sector || !state.card) return;
    if (map.getZoom() < LAYER_MIN_ZOOM) { surfacesLayer.clearLayers(); st.textContent = 'zoom in to see them'; return; }
    if (!navigator.onLine) { surfacesLayer.clearLayers(); st.textContent = 'offline: no colours'; return; }
    st.textContent = 'loading live…';
    try {
      const b = map.getBounds().pad(0.1);
      const surfaces = await PT.liveArea(await ctx(), [b.getSouth(), b.getWest(), b.getNorth(), b.getEast()]);
      if (seq !== state.layerSeq) return;
      surfacesLayer.clearLayers();
      for (const s of surfaces) {
        const c = KIND_STYLE[s.kind].color;
        L.polygon(PT.surfaceLatLngs(s.f.g), { color: c, weight: 1, fillColor: c, fillOpacity: 0.55, interactive: false }).addTo(surfacesLayer);
      }
      st.textContent = 'updated ' + clock();
    } catch (e) {
      if (seq !== state.layerSeq) return;
      surfacesLayer.clearLayers();
      st.textContent = 'not available (' + e.message + ')';
    }
  }
  map.on('moveend', scheduleLayer);
  setInterval(() => { if (map.getZoom() >= LAYER_MIN_ZOOM) refreshLayer(); }, 120000); // keep colours fresh

  // ---------- sheet ----------
  function toast(msg, ms = 3500) {
    const t = $('toast');
    t.textContent = msg; t.classList.remove('hidden');
    clearTimeout(toast.timer); toast.timer = setTimeout(() => t.classList.add('hidden'), ms);
  }
  function syncSheetHeight() { document.documentElement.style.setProperty('--sheet-h', $('sheet').offsetHeight + 'px'); }

  function renderCardLine() {
    const c = state.card, el = $('cardLine');
    if (!c) { el.innerHTML = '<span class="warn">No card yet — tap 🪪 to add yours.</span>'; return; }
    const cs = PT.cardState(c);
    const left = c.to ? daysBetween(todayISO(), c.to) : null;
    let warn = '';
    if (!cs.valid) warn = `<span class="warn">${esc(cs.why)}</span>`;
    else if (left !== null && left <= EXPIRY_WARN_DAYS) {
      warn = `<span class="warn">Expires in ${left} day(s) — <a href="mailto:circulation.dem@brucity.be?subject=${encodeURIComponent('Renouvellement carte riverain ' + (c.number || ''))}">renew</a></span>`;
    }
    if (state.list?.status === 'changed') warn += '<span class="warn">City list changed — update the app</span>';
    const tol = (c.tolerances || []).length ? ' + tolerance' : '';
    el.innerHTML = `<span>🪪 ${esc(c.plate || 'plate?')} · sector <b>${esc(state.sector?.name || c.sector)}</b>${tol} · until ${fmtDate(c.to)}</span>${warn}`;
  }

  const navLinks = (lat, lon) => `<div class="btns">
      <a class="nav waze" target="_blank" rel="noopener" href="https://waze.com/ul?ll=${lat.toFixed(6)},${lon.toFixed(6)}&navigate=yes">Waze</a>
      <a class="nav gmaps" target="_blank" rel="noopener" href="https://www.google.com/maps/dir/?api=1&destination=${lat.toFixed(6)},${lon.toFixed(6)}&travelmode=driving">Google Maps</a>
    </div>`;

  function renderStatus(r, source, accuracy) {
    const box = $('status');
    box.className = 'status ' + r.status;
    box.innerHTML = `<div class="status-title">${r.title}</div><div class="status-detail">${source === 'me' ? '📍 Your position: ' : '📌 Pin: '}${r.detail}</div>`;
    const live = [];
    if (r.zone) live.push('🟢 live parking.brussels');
    live.push('checked ' + clock());
    if (source === 'me' && accuracy != null) live.push(`GPS ±${Math.round(accuracy)} m`);
    $('liveLine').textContent = live.join(' · ');
    syncSheetHeight();
  }

  // Nearest covered spots — computed from live data at this moment.
  async function showFreeSpots(lat, lon) {
    const seq = ++state.spotSeq, near = $('nearest');
    state.lastSpotAt = { lat, lon, t: Date.now() };
    near.innerHTML = '🔎 Searching live for the nearest spot covered by your card…';
    near.classList.remove('hidden'); syncSheetHeight();
    let spots = [];
    try { spots = await PT.liveFreeSpots(await ctx(), lat, lon); } catch (e) {
      if (seq === state.spotSeq) { near.innerHTML = `Could not search live (${esc(e.message)}).`; syncSheetHeight(); }
      return;
    }
    if (seq !== state.spotSeq) return;
    if (!spots.length) { near.innerHTML = 'No spot covered by your card within ~400 m right now.'; syncSheetHeight(); return; }
    const [best, ...others] = spots;
    near.innerHTML = `🟢 Nearest spot covered by your card (live ${clock()}): <b>${esc(best.label)}</b> — ${Math.round(best.m)} m`
      + (others.length ? `<br><small>Also: ${others.map((o) => `${esc(o.label)} (${Math.round(o.m)} m)`).join(', ')}</small>` : '')
      + navLinks(best.lat, best.lon);
    if (targetMarker) map.removeLayer(targetMarker);
    targetMarker = L.circleMarker([best.lat, best.lon], { radius: 11, color: '#16a34a', weight: 3, fillOpacity: 0.25 }).addTo(map);
    syncSheetHeight();
    return best;
  }
  function hideFreeSpots() {
    state.spotSeq++;
    $('nearest').classList.add('hidden');
    if (targetMarker) { map.removeLayer(targetMarker); targetMarker = null; }
    syncSheetHeight();
  }

  async function runCheck(lat, lon, source, accuracy) {
    const seq = ++state.checkSeq;
    state.lastCheck = { lat, lon, t: Date.now(), source, accuracy };
    $('liveLine').textContent = `⏳ checking live (${source === 'me' ? 'your position' : 'pin'})…`;
    let r;
    try { r = await PT.decide(await ctx(), lat, lon, accuracy); } catch (e) { r = PT.cantVerify('Unexpected error: ' + esc(e.message)); }
    if (seq !== state.checkSeq) return r;
    renderStatus(r, source, accuracy);
    renderCardLine();
    if (r.status === 'free') hideFreeSpots();
    else if (r.zone) {
      const ls = state.lastSpotAt;
      const due = !ls || PT.dist(lat, lon, ls.lat, ls.lon) > 60 || Date.now() - ls.t > 60000 || source === 'pin';
      if (due) showFreeSpots(lat, lon);
    }
    if (source === 'me' && r.status !== 'unknown') {
      if (state.lastStatus && r.status !== state.lastStatus) {
        toast(r.status === 'free' ? '🟢 You are now in a spot covered by your card' : '🔴 Not covered here');
        if (navigator.vibrate) navigator.vibrate(r.status === 'free' ? [80, 60, 80] : [300]);
      }
      state.lastStatus = r.status;
    }
    return r;
  }

  // ---------- pin ----------
  function setPin(latlng) {
    state.pin = latlng;
    if (!pinMarker) {
      pinMarker = L.marker(latlng, { draggable: true }).addTo(map);
      pinMarker.on('dragend', () => setPin(pinMarker.getLatLng()));
    } else pinMarker.setLatLng(latlng);
    runCheck(latlng.lat, latlng.lng, 'pin');
  }
  map.on('click', (e) => { if (state.tracking) stopTracking(); setPin(e.latlng); });

  // ---------- live position ----------
  function onPosition(pos) {
    const { latitude: lat, longitude: lon, accuracy } = pos.coords;
    if (Date.now() - pos.timestamp > MAX_FIX_AGE_MS) return;
    state.me = { lat, lon, accuracy, t: Date.now() };
    if (!meMarker) {
      meMarker = L.marker([lat, lon], { icon: L.divIcon({ className: '', html: '<div class="me-dot"></div>', iconSize: [18, 18] }), interactive: false }).addTo(map);
      meAcc = L.circle([lat, lon], { radius: accuracy, color: '#2563eb', weight: 1, fillOpacity: 0.08, interactive: false }).addTo(map);
      map.setView([lat, lon], 18);
    } else {
      meMarker.setLatLng([lat, lon]); meAcc.setLatLng([lat, lon]).setRadius(accuracy);
      if (state.tracking) map.panTo([lat, lon], { animate: true });
    }
    if (!state.tracking) return;
    const lc = state.lastCheck;
    const moved = !lc || lc.source !== 'me' || PT.dist(lat, lon, lc.lat, lc.lon) >= RECHECK_MOVE_M;
    const old = !lc || Date.now() - lc.t >= RECHECK_EVERY_MS;
    const better = lc && lc.accuracy > PT.C.MAX_GPS_ACCURACY_M && accuracy <= PT.C.MAX_GPS_ACCURACY_M;
    if (moved || old || better) runCheck(lat, lon, 'me', accuracy);
  }
  function startTracking() {
    if (!navigator.geolocation) { toast('Location is not available on this device.'); return; }
    state.tracking = true; state.lastStatus = null; state.lastCheck = null;
    $('trackBtn').classList.add('active');
    if (pinMarker) { map.removeLayer(pinMarker); pinMarker = null; state.pin = null; }
    $('liveLine').textContent = '⏳ waiting for GPS…';
    state.watchId = navigator.geolocation.watchPosition(onPosition, (err) => {
      renderStatus(PT.cantVerify('No GPS position: ' + esc(err.message) + '. Allow location for this app.'), 'me');
      stopTracking();
    }, { enableHighAccuracy: true, maximumAge: 0, timeout: 30000 });
  }
  function stopTracking() {
    state.tracking = false;
    $('trackBtn').classList.remove('active');
    if (state.watchId != null) navigator.geolocation.clearWatch(state.watchId);
    state.watchId = null;
  }
  $('trackBtn').onclick = () => (state.tracking ? stopTracking() : startTracking());
  setInterval(() => {
    if (state.tracking && state.me && Date.now() - (state.lastCheck?.t || 0) >= RECHECK_EVERY_MS) {
      if (Date.now() - state.me.t > MAX_FIX_AGE_MS) renderStatus(PT.cantVerify('Lost the GPS signal. Waiting for a fresh position…'), 'me');
      else runCheck(state.me.lat, state.me.lon, 'me', state.me.accuracy);
    }
  }, 5000);

  window.addEventListener('offline', () => {
    renderStatus(PT.cantVerify('Internet connection lost. The app only answers with live data.'), state.tracking ? 'me' : 'pin');
    hideFreeSpots(); refreshLayer();
  });
  window.addEventListener('online', () => { toast('Back online'); recheck(); refreshLayer(); });

  $('findBtn').onclick = async () => {
    const from = state.tracking && state.me ? state.me : state.pin ? { lat: state.pin.lat, lon: state.pin.lng } : state.me;
    if (!from) { toast('Press 📍 or tap the map first.'); return; }
    const best = await showFreeSpots(from.lat, from.lon);
    if (best) map.fitBounds(L.latLngBounds([[from.lat, from.lon], [best.lat, best.lon]]).pad(0.4));
  };

  // ---------- address search ----------
  $('searchForm').onsubmit = async (e) => {
    e.preventDefault();
    const q = $('searchInput').value.trim();
    if (!q) return;
    try {
      const url = `https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=be&viewbox=4.24,50.92,4.48,50.76&bounded=1&q=${encodeURIComponent(q)}`;
      const res = await (await PT.withTimeout(fetch(url, { headers: { 'Accept-Language': 'fr' } }), 12000, 'search')).json();
      if (!res.length) { toast('Address not found.'); return; }
      const ll = L.latLng(+res[0].lat, +res[0].lon);
      if (state.tracking) stopTracking();
      map.setView(ll, 18);
      setPin(ll);
      $('searchInput').blur();
    } catch { toast('Search failed — check your connection.'); }
  };

  // ---------- card dialog ----------
  const dlg = $('cardDialog'), form = $('cardForm');
  $('sectorSelect').innerHTML = Object.values(window.SECTORS).map((s) => `<option value="${s.id}">${esc(s.name)} / ${esc(s.nameNl)}</option>`).join('');
  function renderTolerances(sectorId, checked = []) {
    const tols = window.SECTORS[sectorId]?.tolerances || [];
    $('tolBox').innerHTML = tols.map((t) => `
      <label class="check"><input type="checkbox" name="tol" value="${esc(t.code)}" ${checked.includes(t.code) ? 'checked' : ''} />
        <span>${esc(t.label)}<small>Only if your card shows the code <b>${esc(t.code)}</b>. Not sure? Ask the City (02 279 22 11).</small></span>
      </label>`).join('');
  }
  $('sectorSelect').onchange = () => renderTolerances(form.sector.value);
  function openCard() {
    const c = state.card || {};
    form.number.value = c.number || '';
    form.plate.value = c.plate || '';
    form.sector.value = c.sector || 'louise';
    renderTolerances(form.sector.value, c.tolerances || []);
    form.from.value = c.from || '';
    form.to.value = c.to || '';
    $('scanStatus').textContent = '';
    dlg.showModal();
  }
  $('cardBtn').onclick = openCard;
  dlg.addEventListener('close', () => {
    if (dlg.returnValue !== 'save') return;
    const first = !state.card;
    state.card = {
      number: form.number.value.trim(), plate: form.plate.value.trim().toUpperCase().replace(/[\s-]/g, ''),
      sector: form.sector.value, from: form.from.value, to: form.to.value,
      tolerances: [...form.querySelectorAll('input[name="tol"]:checked')].map((i) => i.value),
    };
    store.set('pt.card', state.card);
    loadSector();
    if (first && !state.tracking) startTracking();
  });

  // OCR of a card photo (Tesseract.js, loaded only when used)
  function loadScript(src) {
    return new Promise((ok, ko) => { const s = document.createElement('script'); s.src = src; s.onload = ok; s.onerror = ko; document.head.appendChild(s); });
  }
  const toISO = (d) => { const [dd, mm, yy] = d.split('/'); return `${yy}-${mm}-${dd}`; };
  function parseCardText(text) {
    const out = {};
    const t = text.replace(/[–—]/g, '-');
    const num = t.match(/\b[B8][O0-9]{3}\s*-\s*\d{3}\s*-\s*\d{3}\b/i);
    if (num) out.number = num[0].replace(/\s/g, '').replace(/^8/, 'B').replace(/^(B)O/i, '$10').replace(/^(B\d)O/i, '$10');
    const range = t.match(/(\d{2}\/\d{2}\/\d{4})\s*-\s*(\d{2}\/\d{2}\/\d{4})/);
    if (range) { out.from = toISO(range[1]); out.to = toISO(range[2]); }
    const plate = t.match(/\b\d[A-Z]{3}\d{3}\b/) || t.match(/\b[A-Z]{3}\d{3}\b/);
    if (plate) out.plate = plate[0];
    for (const s of Object.values(window.SECTORS)) {
      if (new RegExp(`\\b(${s.name}|${s.nameNl})\\b`, 'i').test(t)) { out.sector = s.id; break; }
    }
    const loose = t.toUpperCase().replace(/[\s_\-.]+/g, '_');
    const codes = Object.values(window.SECTORS).flatMap((s) => (s.tolerances || []).map((x) => x.code));
    const found = codes.filter((c) => loose.includes(c));
    if (found.length) out.tolerances = found;
    return out;
  }
  $('scanInput').onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const st = $('scanStatus');
    try {
      st.textContent = 'Loading text recognition…';
      if (!window.Tesseract) await loadScript('https://cdn.jsdelivr.net/npm/tesseract.js@5.1.1/dist/tesseract.min.js');
      st.textContent = 'Reading the card… (can take ~20 s)';
      const { data } = await window.Tesseract.recognize(file, 'fra');
      const f = parseCardText(data.text);
      for (const k of ['number', 'plate', 'sector', 'from', 'to']) if (f[k]) form[k].value = f[k];
      if (f.sector || f.tolerances) {
        const keep = [...form.querySelectorAll('input[name="tol"]:checked')].map((i) => i.value);
        renderTolerances(form.sector.value, [...keep, ...(f.tolerances || [])]);
      }
      const found = Object.keys(f);
      st.textContent = found.length ? `Filled: ${found.join(', ')}. Please double-check.` : 'Could not read the card — please type the fields.';
    } catch (err) {
      st.textContent = 'Scan failed: ' + (err?.message || err);
    } finally { e.target.value = ''; }
  };

  // ---------- start ----------
  function recheck() {
    renderCardLine();
    if (state.tracking && state.me) runCheck(state.me.lat, state.me.lon, 'me', state.me.accuracy);
    else if (state.pin) runCheck(state.pin.lat, state.pin.lng, 'pin');
  }
  function loadSector() {
    const def = window.SECTORS[state.card?.sector || 'louise'];
    if (!def) return;
    state.sector = PT.buildSector(def);
    state.list = null;
    renderCardLine();
    ctx().then(() => { renderCardLine(); recheck(); refreshLayer(); });
  }

  window.__pt = { state, map, runCheck, ctx, parseCardText, refreshLayer, showFreeSpots };

  if ('serviceWorker' in navigator && location.protocol !== 'file:' && !PT.IN_APP) {
    navigator.serviceWorker.register('sw.js').catch(() => {});
  }
  renderCardLine();
  syncSheetHeight();
  window.addEventListener('resize', syncSheetHeight);
  loadSector();
  if (!state.card) setTimeout(openCard, 300);
  else startTracking();
})();
