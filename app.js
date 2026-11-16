/* Parking Tracker — screen and interactions. All data and decisions come from live.js (window.PT). */
(() => {
  'use strict';
  const PT = window.PT;
  const { esc, fmtDate, todayISO } = PT;

  const EXPIRY_WARN_DAYS = 30;
  const MAX_FIX_AGE_MS = 15000;
  const RECHECK_MOVE_M = 8;
  const RECHECK_EVERY_MS = 30000;
  const LAYER_MIN_ZOOM = 16; // house-number detail (City street lists)
  const CARD_MIN_ZOOM = 15; // your card's coverage per street surface
  const KIND_STYLE = {
    covered: { color: '#16a34a', label: 'free with your card' },
    mixed: { color: '#f59e0b', label: 'partly (check the number)' },
    pay: { color: '#dc2626', label: 'pay' },
    red: { color: '#7f1d1d', label: 'red zone (card never valid)' },
    disc: { color: '#2563eb', label: 'blue zone (parking disc)' },
    free: { color: '#0d9488', label: 'free for everyone' },
    freenow: { color: '#2dd4bf', label: 'free right now (outside paid hours)' },
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
    // Only the City of Brussels uses documents (street lists) that must be confirmed unchanged.
    if (stale && state.sector) {
      listPromise = listPromise || PT.checkOfficialList(state.sector).then((l) => { state.list = l; listPromise = null; return l; });
      await listPromise;
    }
    return {
      card: state.card, muni: state.muni || MUNI.bruxelles, sector: state.sector,
      codes: new Set(state.card?.tolerances || []), mailles: new Set(state.card?.mailles || []), lims: new Set(state.card?.lims || []),
      listProblem: state.sector ? PT.listProblem(state.list) : null,
    };
  }
  const MUNI = Object.fromEntries(window.MUNICIPALITIES.map((m) => [m.id, m]));
  // Cards saved by older versions only had a City sector.
  if (state.card && !state.card.municipality) { state.card.municipality = 'bruxelles'; store.set('pt.card', state.card); }

  // ---------- map ----------
  const map = L.map('map', { zoomControl: false, preferCanvas: true, attributionControl: true }).setView([50.8285, 4.3640], 16);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> · parking.brussels',
  }).addTo(map);
  const surfacesLayer = L.layerGroup().addTo(map);
  let meMarker = null, meAcc = null, pinMarker = null, targetMarker = null;
  const ICON = window.ICON;
  const divIcon = (cls, html, size, anchor) => L.divIcon({ className: '', html: `<div class="${cls}">${html}</div>`, iconSize: size, iconAnchor: anchor });
  const PIN_ICON = divIcon('pin-marker', ICON('pin'), [38, 38], [19, 36]);
  const CAR_ICON = divIcon('car-marker', ICON('car'), [36, 36], [18, 18]);
  const TARGET_ICON = divIcon('target-marker', ICON('parking'), [30, 30], [15, 15]);

  // ---------- live map layers ----------
  // Zoomed out: the official zone colours drawn by parking.brussels' own map server (cheap at any zoom).
  const zonesWms = L.tileLayer.wms(PT.IN_APP ? 'https://ows.parking.brussels/ws/ParkingBrussels/ows' : '/pbapi/ws/ParkingBrussels/ows', {
    layers: 'ParkingBrussels:regulation_zones', format: 'image/png', transparent: true, opacity: 0.75, maxZoom: 19, attribution: 'zones © parking.brussels',
  });
  const myAreasLayer = L.layerGroup().addTo(map);
  const LAYERS = {
    streets: { minZoom: CARD_MIN_ZOOM, group: surfacesLayer, on: true },
    spaces: { minZoom: 17, group: L.layerGroup().addTo(map), on: true },
    works: { minZoom: 15, group: L.layerGroup().addTo(map), on: true },
    carparks: { minZoom: 14, group: L.layerGroup().addTo(map), on: false },
    chargers: { minZoom: 15, group: L.layerGroup().addTo(map), on: false },
  };
  const savedLayers = store.get('pt.layers') || {};
  for (const [k, v] of Object.entries(savedLayers)) if (LAYERS[k]) LAYERS[k].on = !!v;
  const poi = (emoji) => L.divIcon({ className: '', html: `<div class="poi-icon">${emoji}</div>`, iconSize: [22, 22], iconAnchor: [11, 11] });
  const DRAW = {
    async streets(box) {
      const c = await ctx();
      const surfaces = await PT.liveArea(c, box, { withAddresses: map.getZoom() >= LAYER_MIN_ZOOM });
      // Outline of the areas (mailles) on your card.
      let areas = [];
      if (c.muni.kind === 'mailles' && c.mailles.size) {
        try { areas = (await PT.liveMailles(box)).filter((m) => c.mailles.has(m.p.zonecode) || c.lims.has(m.p.zonecode)); } catch { /* outline only */ }
      }
      return (g) => {
        myAreasLayer.clearLayers();
        for (const a of areas) {
          L.polygon(PT.surfaceLatLngs(a.g), { color: '#1d4ed8', weight: 3, dashArray: '8 6', fill: false, interactive: false }).addTo(myAreasLayer);
        }
        for (const s of surfaces) {
          let kind = s.kind;
          // Outside paid hours: streets you'd pay on are free for everyone right now.
          const rule = s.rule;
          if (['pay', 'red', 'disc', 'mixed'].includes(kind) && rule && !PT.paidNow(rule)) kind = kind === 'mixed' ? 'covered' : 'freenow';
          const c = KIND_STYLE[kind].color;
          L.polygon(PT.surfaceLatLngs(s.f.g), { color: c, weight: 1, fillColor: c, fillOpacity: 0.55, interactive: false }).addTo(g);
        }
      };
    },
    async spaces(box) {
      const spaces = await PT.liveSpaces(box);
      return (g) => {
        for (const s of spaces) {
          if (s.typres) {
            L.marker([s.lat, s.lon], { icon: poi(s.label ? s.label.split(' ')[0] : '⛔'), interactive: true })
              .bindPopup(`<b>${esc(s.label || 'Reserved space')}</b><br>You can't park here with your residents' card.`).addTo(g);
          } else {
            L.circleMarker([s.lat, s.lon], { radius: 2.5, color: '#334155', weight: 1, fillColor: '#fff', fillOpacity: 1, interactive: false }).addTo(g);
          }
        }
      };
    },
    async works(box) {
      const works = await PT.liveWorks(box);
      return (g) => {
        for (const w of works) {
          const html = `<b>🚧 ${esc(w.title)}</b><br>${esc(w.where)}<br>${w.from ? 'From ' + esc(fmtDate(w.from)) : ''}${w.to ? ' until ' + esc(fmtDate(w.to)) : ''}`
            + `${w.by ? '<br>' + esc(w.by) : ''}${w.link ? `<br><a href="${esc(w.link)}" target="_blank" rel="noopener">Details</a>` : ''}`;
          if (w.g.type === 'Point') {
            const [x, y] = w.g.coordinates; const ll = fromMerc(x, y);
            L.marker(ll, { icon: poi('🚧') }).bindPopup(html).addTo(g);
          } else {
            L.polygon(PT.surfaceLatLngs(w.g), { color: '#ea580c', weight: 2, dashArray: '4 3', fillColor: '#fb923c', fillOpacity: 0.25 }).bindPopup(html).addTo(g);
          }
        }
      };
    },
    async carparks(box) {
      const parks = await PT.liveCarParks(box);
      return (g) => {
        for (const p of parks) {
          L.marker([p.lat, p.lon], { icon: poi('🅿️') }).bindPopup(`<b>${esc(p.name)}</b><br>${esc(p.address)}<br>${p.capacity ? esc(p.capacity) + ' places' : ''}`
            + `${p.disabled ? ', ' + esc(p.disabled) + ' ♿' : ''}${p.maxheight ? '<br>Max height ' + esc(p.maxheight) + ' m' : ''}`
            + `${p.operator ? '<br>' + esc(p.operator) : ''}${p.phone ? ` · <a href="tel:${esc(p.phone)}">${esc(p.phone)}</a>` : ''}${navLinks(p.lat, p.lon)}`).addTo(g);
        }
      };
    },
    async chargers(box) {
      const ch = await PT.liveChargers(box);
      return (g) => {
        for (const c of ch) {
          L.marker([c.lat, c.lon], { icon: poi('⚡') }).bindPopup(`<b>⚡ ${esc(c.operator || 'Charging point')}</b><br>${esc(c.points || '?')} point(s) · ${esc(c.power || '?')} V`
            + `<br>${esc(c.standard || '')} ${esc(c.type || '')}${navLinks(c.lat, c.lon)}`).addTo(g);
        }
      };
    },
  };
  const fromMerc = (x, y) => [(2 * Math.atan(Math.exp(y / 6378137)) - Math.PI / 2) * 180 / Math.PI, (x / 6378137) * 180 / Math.PI];

  let layerTimer = null;
  function scheduleLayer() { clearTimeout(layerTimer); layerTimer = setTimeout(refreshLayer, 700); }
  async function refreshLayer() {
    const seq = ++state.layerSeq, st = $('layerState');
    if (!state.card || !state.muni) return;
    if (!navigator.onLine) { for (const l of Object.values(LAYERS)) l.group.clearLayers(); map.removeLayer(zonesWms); st.textContent = 'offline: no live layers'; return; }
    const b = map.getBounds().pad(0.1), box = [b.getSouth(), b.getWest(), b.getNorth(), b.getEast()], z = map.getZoom();
    // Zoomed out: official zone colours (parking.brussels map images). Closer: your card's coverage per street.
    if (LAYERS.streets.on && z < CARD_MIN_ZOOM) zonesWms.addTo(map); else map.removeLayer(zonesWms);
    if (!LAYERS.streets.on || z < CARD_MIN_ZOOM) myAreasLayer.clearLayers();
    const jobs = Object.entries(LAYERS).map(async ([name, l]) => {
      if (!l.on || z < l.minZoom) { l.group.clearLayers(); return null; }
      try {
        const draw = await DRAW[name](box);
        if (seq !== state.layerSeq) return null;
        l.group.clearLayers(); draw(l.group); return null;
      } catch (e) { if (seq === state.layerSeq) l.group.clearLayers(); return `${name}: ${e.message}`; }
    });
    st.textContent = 'loading live…';
    const errors = (await Promise.all(jobs)).filter(Boolean);
    if (seq !== state.layerSeq) return;
    st.textContent = errors.length ? 'some layers failed: ' + errors.join('; ')
      : z < CARD_MIN_ZOOM ? 'Official zone colours (red, grey, green, blue…). Zoom in to see where your card works.'
        : z < 17 ? `Where your card works · updated ${clock()} · zoom in for parking spaces`
          : `Where your card works + parking spaces · updated ${clock()}`;
  }
  map.on('moveend', scheduleLayer);
  setInterval(refreshLayer, 120000); // keep layers fresh

  // Layers panel
  $('layersBtn').onclick = () => {
    const open = $('layersPanel').classList.toggle('hidden') === false;
    if (open) { $('sheet').classList.add('compact'); syncSheetHeight(); }
  };
  map.on('click', () => $('layersPanel').classList.add('hidden'));
  for (const cb of document.querySelectorAll('#layersPanel input[data-layer]')) {
    cb.checked = LAYERS[cb.dataset.layer].on;
    cb.onchange = () => {
      LAYERS[cb.dataset.layer].on = cb.checked;
      store.set('pt.layers', Object.fromEntries(Object.entries(LAYERS).map(([k, v]) => [k, v.on])));
      refreshLayer();
    };
  }

  // ---------- sheet ----------
  function toast(msg, ms = 3500) {
    const t = $('toast');
    t.textContent = msg; t.classList.remove('hidden');
    clearTimeout(toast.timer); toast.timer = setTimeout(() => t.classList.add('hidden'), ms);
  }
  function syncSheetHeight() {
    const h = window.innerWidth >= 720 ? 0 : $('sheet').offsetHeight;
    document.documentElement.style.setProperty('--sheet-h', h + 'px');
  }
  $('sheetHandle').onclick = () => { $('sheet').classList.toggle('compact'); syncSheetHeight(); };

  function renderCardLine() {
    const c = state.card, el = $('cardLine'), dot = $('cardDot');
    if (!c) {
      dot.className = 'dot none';
      el.innerHTML = `<span class="pill">${ICON('card')}No card yet</span><a href="#" id="addCardLink">Add my card</a>`;
      $('addCardLink').onclick = (e) => { e.preventDefault(); openCard(); };
      return;
    }
    const cs = PT.cardState(c);
    const left = c.to ? daysBetween(todayISO(), c.to) : null;
    let right = `<span>Valid until ${fmtDate(c.to)}</span>`;
    if (!cs.valid) right = `<span class="warn">${esc(cs.why)}</span>`;
    else if (left !== null && left <= EXPIRY_WARN_DAYS) {
      right = `<span class="warn">Expires in ${left} day(s) · <a href="mailto:circulation.dem@brucity.be?subject=${encodeURIComponent('Renouvellement carte riverain ' + (c.number || ''))}">renew</a></span>`;
    }
    if (state.list?.status === 'changed') right = '<span class="warn">City list changed — update the app</span>';
    dot.className = 'dot' + (cs.valid && state.list?.status !== 'changed' ? '' : ' bad');
    const tol = (c.tolerances || []).length ? ' + Ixelles' : '';
    const muni = MUNI[c.municipality] || MUNI.bruxelles;
    const where = muni.kind === 'streets' ? (state.sector?.name || c.sector)
      : muni.kind === 'mailles' ? `${muni.name}${(c.mailles || []).length ? ' · ' + c.mailles.length + ' area' + (c.mailles.length > 1 ? 's' : '') : ' · choose your areas'}` : muni.name;
    el.innerHTML = `<span class="pill">${ICON('card')}${esc(where)}${tol}${c.plate ? ' · ' + esc(c.plate) : ''}</span>${right}`;
  }

  const navLinks = (lat, lon) => `<div class="nav-row">
      <a class="btn btn-waze" target="_blank" rel="noopener" href="https://waze.com/ul?ll=${lat.toFixed(6)},${lon.toFixed(6)}&navigate=yes">${ICON('navigation')}Waze</a>
      <a class="btn btn-gmaps" target="_blank" rel="noopener" href="https://www.google.com/maps/dir/?api=1&destination=${lat.toFixed(6)},${lon.toFixed(6)}&travelmode=driving">${ICON('directions')}Google Maps</a>
    </div>`;

  // Friendly headline + icon for each answer.
  const HEADLINES = {
    'FREE ✓': ['Free for you here', 'check'],
    'FREE NOW': ['Free for everyone right now', 'check'],
    PAY: ['Paid parking', 'euro'],
    RESERVED: ['Reserved space', 'block'],
    'CHECK THE NUMBER': ['Check the house number', 'warning'],
    'TIME LIMITED': ['Time-limited parking', 'timer'],
    'PARKING DISC': ['Blue zone: use your disc', 'timer'],
    "CAN'T VERIFY": ["Can't verify right now", 'help'],
  };
  const ZONE_COLORS = { 0: '#0d9488', 1: '#7f1d1d', 2: '#f97316', 3: '#94a3b8', 4: '#22c55e', 5: '#2563eb', 6: '#a855f7', 9: '#0f766e' };
  // Payment happens in the operators' own apps; these buttons open them.
  const PAY_APPS = [['4411', 'https://www.4411.io'], ['EasyPark', 'https://www.easypark.com'], ['Seety', 'https://seety.co']];

  function renderStatus(r, source, accuracy) {
    const box = $('status');
    const [title, icon] = HEADLINES[r.title] || [r.title, 'help'];
    box.className = 'result ' + r.status;
    const notes = (r.notes || []).length ? `<ul class="notes">${r.notes.map((n) => `<li><span>${n}</span></li>`).join('')}</ul>` : '';
    const actions = [];
    if (r.status === 'free') {
      actions.push(`<button type="button" class="btn btn-primary btn-sm" data-act="park">${ICON('car')}I parked here</button>`);
    }
    if (r.status === 'pay' && r.zone && !r.reserved) {
      actions.push(`<span class="paylinks">Pay with ${PAY_APPS.map(([n, u]) => `<a class="btn btn-sm" href="${u}" target="_blank" rel="noopener">${n}</a>`).join('')}</span>`);
    }
    if (r.status !== 'free' && r.zone) {
      actions.push(`<button type="button" class="btn btn-sm" data-act="park">${ICON('timer')}Park &amp; set timer</button>`);
    }
    state.lastResult = r;
    const tariff = r.tariff && (r.status !== 'free' || r.title === 'FREE NOW')
      ? `<div class="tariff">${ICON('euro')}<span>${esc(r.tariff)}</span></div>` : '';
    box.innerHTML = `<div class="hero"><div class="hero-icon">${ICON(icon)}</div><div class="hero-text">
        <div class="hero-title">${esc(title)}</div><div class="hero-sub">${r.detail}</div></div></div>
      ${tariff}${notes}${actions.length ? `<div class="actions">${actions.join('')}</div>` : ''}`;
    const chips = [`<span class="chip${r.zone ? ' live' : ''}">${ICON('live')}${r.zone ? 'Live' : 'Checked'} · ${clock()}</span>`];
    chips.push(`<span class="chip">${ICON(source === 'me' ? 'navigation' : 'pin')}${source === 'me' ? 'My position' : 'Pin'}${source === 'me' && accuracy != null ? ` ±${Math.round(accuracy)} m` : ''}</span>`);
    if (r.zone) {
      const z = r.zone;
      chips.push(`<span class="chip zone-chip"><i style="background:${ZONE_COLORS[z.typreg] || '#94a3b8'}"></i>${esc(PT.ZONE_NAMES[z.typreg] || 'unknown')} zone · ${esc(String(z.mu_name_fr || '').toLowerCase().replace(/^./, (m) => m.toUpperCase()))}</span>`);
    }
    $('liveLine').innerHTML = chips.join('');
    syncSheetHeight();
  }
  $('status').addEventListener('click', (e) => {
    const b = e.target.closest('[data-act]');
    if (b?.dataset.act === 'park') { saveCarHere(); openCar(); }
  });

  // Nearest covered spots — computed from live data at this moment.
  async function showFreeSpots(lat, lon) {
    const seq = ++state.spotSeq, near = $('nearest');
    state.lastSpotAt = { lat, lon, t: Date.now() };
    near.innerHTML = '<div class="spot-loading"><span class="spinner"></span>Looking for the nearest spot your card covers…</div>';
    near.classList.remove('hidden'); syncSheetHeight();
    let spots = [];
    try { spots = await PT.liveFreeSpots(await ctx(), lat, lon); } catch (e) {
      if (seq === state.spotSeq) { near.innerHTML = `<div class="spot-loading">Could not search right now (${esc(e.message)}).</div>`; syncSheetHeight(); }
      return;
    }
    if (seq !== state.spotSeq) return;
    if (!spots.length) { near.innerHTML = '<div class="spot-loading">No spot covered by your card within ~400 m right now.</div>'; syncSheetHeight(); return; }
    const [best, ...others] = spots;
    const walk = Math.max(1, Math.round(best.m / 80));
    near.innerHTML = `<div class="spot-head"><div class="spot-icon">${ICON('parking')}</div><div>
        <div class="spot-title">${esc(best.label)}</div>
        <div class="spot-sub">Free with your card · ${Math.round(best.m)} m · about ${walk} min on foot</div></div></div>
      ${navLinks(best.lat, best.lon)}
      ${others.length ? `<div class="spot-more">Also nearby: ${others.map((o) => `${esc(o.label)} (${Math.round(o.m)} m)`).join(' · ')}</div>` : ''}`;
    if (targetMarker) map.removeLayer(targetMarker);
    targetMarker = L.marker([best.lat, best.lon], { icon: TARGET_ICON }).addTo(map);
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
    $('status').classList.add('checking');
    $('liveLine').innerHTML = `<span class="chip"><span class="spinner" style="width:12px;height:12px;border-width:2px"></span>Checking live…</span>`;
    let r;
    try { r = await PT.decide(await ctx(), lat, lon, accuracy); } catch (e) { r = PT.cantVerify('Unexpected error: ' + esc(e.message)); }
    if (seq !== state.checkSeq) return r;
    $('status').classList.remove('checking');
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
      pinMarker = L.marker(latlng, { draggable: true, icon: PIN_ICON }).addTo(map);
      pinMarker.on('dragend', () => setPin(pinMarker.getLatLng()));
    } else pinMarker.setLatLng(latlng);
    runCheck(latlng.lat, latlng.lng, 'pin');
  }
  map.on('click', (e) => { if (state.tracking) stopTracking(); setPin(e.latlng); });

  // ---------- live position ----------
  // In the Android app, positions come from a background-capable watcher (foreground service with a
  // small notification) so parking can be detected even when the app is not on screen.
  const BG = () => (PT.IN_APP ? window.Capacitor?.Plugins?.BackgroundGeolocation || window.Capacitor?.registerPlugin?.('BackgroundGeolocation') : null);
  const alertsOn = () => store.get('pt.bgAlerts') !== false; // default: on
  function onPosition(pos) {
    const { latitude: lat, longitude: lon, accuracy } = pos.coords;
    if (Date.now() - pos.timestamp > MAX_FIX_AGE_MS) return;
    parkDetector(lat, lon, accuracy, pos.coords.speed, pos.timestamp);
    state.me = { lat, lon, accuracy, t: Date.now() };
    if (document.visibilityState === 'hidden') return; // in the background only the parking detector runs
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
  function onGpsError(message) {
    // Don't overwrite the answer for a pin the user dropped meanwhile.
    if (!state.pin) renderStatus(PT.cantVerify('No GPS position: ' + esc(message) + '. Allow location for this app, or tap the map.'), 'me');
    else toast('No GPS: showing the pin you dropped');
    stopTracking(true);
    renderPermission();
  }
  // Starts the position source if needed (one watcher for the whole app).
  async function ensureWatcher() {
    if (state.watchId != null) return;
    const bg = BG();
    if (bg) {
      state.watchKind = 'bg';
      state.watchId = 'starting';
      try {
        state.watchId = await bg.addWatcher({
          backgroundTitle: 'Parking Tracker',
          backgroundMessage: alertsOn() ? 'Watching where you park, to tell you if it is free or paid.' : undefined,
          requestPermissions: true, stale: false, distanceFilter: 5,
        }, (loc, err) => {
          if (err) { onGpsError(err.code === 'NOT_AUTHORIZED' ? 'location not allowed' : err.message || 'error'); return; }
          if (loc) onPosition({ coords: { latitude: loc.latitude, longitude: loc.longitude, accuracy: loc.accuracy, speed: loc.speed }, timestamp: loc.time || Date.now() });
        });
      } catch (e) { state.watchId = null; onGpsError(e.message || 'location unavailable'); }
      return;
    }
    if (!navigator.geolocation) { toast('Location is not available on this device.'); return; }
    state.watchKind = 'web';
    state.watchId = navigator.geolocation.watchPosition(onPosition, (err) => onGpsError(err.message),
      { enableHighAccuracy: true, maximumAge: 0, timeout: 30000 });
  }
  async function removeWatcher() {
    const id = state.watchId;
    state.watchId = null;
    if (id == null || id === 'starting') return;
    if (state.watchKind === 'bg') { try { await BG()?.removeWatcher({ id }); } catch { /* already gone */ } } else navigator.geolocation.clearWatch(id);
  }
  function startTracking() {
    state.tracking = true; state.lastStatus = null; state.lastCheck = null;
    $('trackBtn').classList.add('active');
    $('trackBtn').innerHTML = ICON('navigation');
    if (pinMarker) { map.removeLayer(pinMarker); pinMarker = null; state.pin = null; }
    if (state.me) { map.setView([state.me.lat, state.me.lon], Math.max(map.getZoom(), 17)); runCheck(state.me.lat, state.me.lon, 'me', state.me.accuracy); }
    else $('liveLine').innerHTML = `<span class="chip"><span class="spinner" style="width:12px;height:12px;border-width:2px"></span>Finding your position…</span>`;
    ensureWatcher();
  }
  // Leaving "follow me": the background watcher keeps running for parking alerts (unless there was an error).
  function stopTracking(failed) {
    state.tracking = false;
    $('trackBtn').classList.remove('active');
    $('trackBtn').innerHTML = ICON('locate');
    if (failed || state.watchKind !== 'bg' || !alertsOn()) removeWatcher();
  }
  $('trackBtn').onclick = () => (state.tracking ? stopTracking() : startTracking());
  setInterval(() => {
    if (document.visibilityState === 'hidden') return;
    if (state.tracking && state.me && Date.now() - (state.lastCheck?.t || 0) >= RECHECK_EVERY_MS) {
      // The app's watcher only reports when you move (5 m), so no news means you're still there.
      if (state.watchKind !== 'bg' && Date.now() - state.me.t > MAX_FIX_AGE_MS) renderStatus(PT.cantVerify('Lost the GPS signal. Waiting for a fresh position…'), 'me');
      else runCheck(state.me.lat, state.me.lon, 'me', state.me.accuracy);
    }
  }, 5000);

  // ---------- parking detection + alerts ----------
  // Driving = faster than ~20 km/h. Parked = after driving, no movement for 2 minutes.
  const DRIVE_SPEED = 5.5; // m/s
  const PARKED_AFTER_MS = 120000;
  const park = { drove: false, lastFix: null, lastMoveT: 0, lastDriveT: 0 };
  function parkDetector(lat, lon, accuracy, speed, t) {
    const prev = park.lastFix;
    let v = speed;
    if ((v == null || v < 0) && prev && t > prev.t) v = PT.dist(lat, lon, prev.lat, prev.lon) / ((t - prev.t) / 1000);
    if (!prev || PT.dist(lat, lon, prev.lat, prev.lon) > 15) park.lastMoveT = t;
    if (v != null && v >= DRIVE_SPEED) { park.drove = true; park.lastDriveT = t; }
    park.lastFix = { lat, lon, accuracy, t };
  }
  setInterval(() => {
    const f = park.lastFix;
    if (!park.drove || !f || !alertsOn()) return;
    const now = Date.now();
    if (now - park.lastMoveT >= PARKED_AFTER_MS && now - park.lastDriveT < 20 * 60000) {
      park.drove = false;
      onParked(f.lat, f.lon, f.accuracy);
    }
  }, 15000);

  const LNP = () => window.Capacitor?.Plugins?.LocalNotifications;
  async function notify(id, title, body) {
    const ln = LNP();
    try {
      if (ln) { await ln.schedule({ notifications: [{ id, title, body, largeBody: body }] }); return; }
      if ('Notification' in window && Notification.permission === 'granted') { new Notification(title, { body }); return; }
    } catch { /* fall back to a toast */ }
    toast(title + ' — ' + body, 8000);
  }
  const plain = (html) => String(html || '').replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&#39;/g, "'").replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
  async function onParked(lat, lon, accuracy) {
    const r = await PT.decide(await ctx(), lat, lon, Math.min(accuracy || 10, PT.C.MAX_GPS_ACCURACY_M));
    const [title] = HEADLINES[r.title] || [r.title];
    store.set('pt.car', { lat, lon, t: Date.now(), status: title, where: '' });
    drawCar();
    if (document.visibilityState === 'visible') { toast(`🚗 Parked: ${title}`); if (!state.tracking) runCheck(lat, lon, 'me', accuracy); return; }
    let body = plain(r.detail);
    if (r.tariff && r.status !== 'free') body += ` · ${r.tariff}`;
    if (r.status !== 'free' && r.zone) {
      try {
        const [spot] = await PT.liveFreeSpots(await ctx(), lat, lon, 1);
        if (spot) body += ` · Nearest free spot for you: ${spot.label}, ${Math.round(spot.m)} m.`;
      } catch { /* no suggestion */ }
    }
    const icon = r.status === 'free' ? '✅' : r.status === 'pay' ? '⚠️' : 'ℹ️';
    await notify(4300 + (Date.now() % 1000), `${icon} ${title}`, body);
  }

  window.addEventListener('offline', () => {
    renderStatus(PT.cantVerify('Internet connection lost. The app only answers with live data.'), state.tracking ? 'me' : 'pin');
    hideFreeSpots(); refreshLayer();
  });
  window.addEventListener('online', () => { toast('Back online'); recheck(); refreshLayer(); });

  $('findBtn').onclick = async () => {
    const from = state.tracking && state.me ? state.me : state.pin ? { lat: state.pin.lat, lon: state.pin.lng } : state.me;
    if (!from) { toast('Turn on my location, or tap the map first'); if (!state.tracking) startTracking(); return; }
    $('sheet').classList.remove('compact');
    const best = await showFreeSpots(from.lat, from.lon);
    if (best) map.fitBounds(L.latLngBounds([[from.lat, from.lon], [best.lat, best.lon]]).pad(0.4));
  };

  // ---------- my car + parking timer ----------
  let carMarker = null;
  const LN = () => window.Capacitor?.Plugins?.LocalNotifications;
  function drawCar() {
    const car = store.get('pt.car');
    if (carMarker) { map.removeLayer(carMarker); carMarker = null; }
    if (car) {
      carMarker = L.marker([car.lat, car.lon], { icon: CAR_ICON })
        .bindPopup(`<b>My car</b><br>Parked ${new Date(car.t).toLocaleString('fr-BE', { dateStyle: 'short', timeStyle: 'short' })}`).addTo(map);
    }
    $('carBtn').classList.toggle('has-car', !!car);
    renderTimer();
  }
  function renderCarDialog() {
    const car = store.get('pt.car'), timer = store.get('pt.timer');
    $('carInfo').innerHTML = car
      ? `Parked ${new Date(car.t).toLocaleString('fr-BE', { dateStyle: 'short', timeStyle: 'short' })}${car.where ? ` · ${car.where}` : ''}${car.status ? `<br>The app said: <b>${esc(car.status)}</b>` : ''}`
      : 'Save where you park, so you can find your car again and get a reminder before your time runs out.';
    $('carActions').innerHTML = car
      ? `<a class="btn btn-primary btn-block" target="_blank" rel="noopener" href="https://www.google.com/maps/dir/?api=1&destination=${car.lat.toFixed(6)},${car.lon.toFixed(6)}&travelmode=walking">${ICON('walk')}Walk to my car</a>
         <div class="grid2"><button type="button" class="btn" id="carShow">${ICON('pin')}Show on map</button>
         <button type="button" class="btn" id="carHere">${ICON('locate')}Car is here now</button></div>
         <button type="button" class="btn btn-ghost btn-block" id="carClear">I left — forget this spot</button>`
      : `<button type="button" id="carHere" class="btn btn-primary btn-block">${ICON('car')}My car is parked here</button>`;
    $('timerStop').classList.toggle('hidden', !timer);
    const bind = (id, fn) => { const el = $(id); if (el) el.onclick = fn; };
    bind('carHere', saveCarHere);
    bind('carShow', () => { const c = store.get('pt.car'); $('carDialog').close(); map.setView([c.lat, c.lon], 18); carMarker?.openPopup(); });
    bind('carClear', () => { store.set('pt.car', null); stopTimer(); drawCar(); renderCarDialog(); });
  }
  function saveCarHere() {
    const at = state.tracking && state.me ? { lat: state.me.lat, lon: state.me.lon } : state.pin ? { lat: state.pin.lat, lon: state.pin.lng } : state.me;
    if (!at) { toast('No position yet: turn on my location or tap the map'); return; }
    const box = $('status');
    store.set('pt.car', { ...at, t: Date.now(), status: box.querySelector('.hero-title')?.textContent || '', where: box.querySelector('.hero-sub b')?.outerHTML || '' });
    drawCar(); renderCarDialog(); toast('Car position saved');
  }
  async function startTimer() {
    const minutes = Math.max(5, Math.min(1440, +$('carForm').minutes.value || 0));
    const end = Date.now() + minutes * 60000;
    const plan = [{ id: 4101, at: end - 10 * 60000, body: 'Your parking time ends in 10 minutes.' }, { id: 4102, at: end, body: 'Your parking time is over.' }]
      .filter((n) => n.at > Date.now() + 5000);
    const ln = LN();
    try {
      if (ln) {
        const p = await ln.requestPermissions();
        if (p.display !== 'granted') { toast('Allow notifications for the reminder.'); return; }
        await ln.cancel({ notifications: [{ id: 4101 }, { id: 4102 }] }).catch(() => {});
        await ln.schedule({ notifications: plan.map((n) => ({ id: n.id, title: '🅿️ Parking Tracker', body: n.body, schedule: { at: new Date(n.at), allowWhileIdle: true } })) });
      } else if ('Notification' in window) {
        if (Notification.permission !== 'granted') await Notification.requestPermission();
        state.webTimers = plan.map((n) => setTimeout(() => { try { new Notification('🅿️ Parking Tracker', { body: n.body }); } catch { toast(n.body, 8000); } }, n.at - Date.now()));
      }
    } catch (e) { toast('Could not schedule the reminder: ' + e.message); return; }
    store.set('pt.timer', { end, minutes });
    renderTimer(); renderCarDialog();
    toast(`Timer set: ${minutes >= 60 ? Math.floor(minutes / 60) + ' h ' + (minutes % 60 ? minutes % 60 + ' min' : '') : minutes + ' min'}`);
  }
  async function stopTimer() {
    store.set('pt.timer', null);
    (state.webTimers || []).forEach(clearTimeout);
    try { await LN()?.cancel({ notifications: [{ id: 4101 }, { id: 4102 }] }); } catch { /* nothing scheduled */ }
    renderTimer(); renderCarDialog();
  }
  function renderTimer() {
    const t = store.get('pt.timer'), car = store.get('pt.car'), el = $('timerLine'), chip = $('carChip');
    if (!t) {
      el.classList.add('hidden');
      chip.classList.toggle('hidden', !car);
      chip.classList.remove('late');
      chip.innerHTML = `${ICON('car')}My car`;
      syncSheetHeight(); return;
    }
    const left = t.end - Date.now();
    const hm = `${Math.floor(left / 3600000)}h${String(Math.floor((left % 3600000) / 60000)).padStart(2, '0')}`;
    const until = new Date(t.end).toLocaleTimeString('fr-BE', { hour: '2-digit', minute: '2-digit' });
    el.classList.remove('hidden');
    el.innerHTML = left > 0 ? `${ICON('timer')}<span>Parking time left <b>${hm}</b> · until ${until}</span>` : `${ICON('timer')}<b>Your parking time is over.</b>`;
    chip.classList.remove('hidden');
    chip.classList.toggle('late', left <= 10 * 60000);
    chip.innerHTML = `${ICON('timer')}${left > 0 ? hm + ' left' : 'Time is over'}`;
    syncSheetHeight();
  }
  setInterval(renderTimer, 30000);
  const openCar = () => {
    // Default timer = the zone's maximum stay (from the live rules), when known.
    const max = +state.lastResult?.rule?.max_time;
    if (max && !store.get('pt.timer')) {
      $('carForm').minutes.value = max;
      for (const x of $('timerPresets').children) x.classList.toggle('on', +x.dataset.min === max);
    }
    renderCarDialog(); $('carDialog').showModal();
  };
  $('carBtn').onclick = openCar;
  $('carChip').onclick = openCar;
  $('timerStart').onclick = startTimer;
  $('timerStop').onclick = stopTimer;
  $('timerPresets').onclick = (e) => {
    const b = e.target.closest('button[data-min]');
    if (!b) return;
    $('carForm').minutes.value = b.dataset.min;
    for (const x of $('timerPresets').children) x.classList.toggle('on', x === b);
  };
  $('layersPanel').addEventListener('click', (e) => { if (e.target.closest('[data-close]')) $('layersPanel').classList.add('hidden'); });

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
  $('muniSelect').innerHTML = window.MUNICIPALITIES.map((m) => `<option value="${m.id}">${esc(m.name)}${m.nameNl !== m.name ? ' / ' + esc(m.nameNl) : ''}</option>`).join('');
  function renderTolerances(sectorId, checked = []) {
    const tols = (form.municipality.value === 'bruxelles' && window.SECTORS[sectorId]?.tolerances) || [];
    $('tolBox').innerHTML = tols.map((t) => `
      <label class="check"><input type="checkbox" name="tol" value="${esc(t.code)}" ${checked.includes(t.code) ? 'checked' : ''} />
        <span><b>${esc(t.label)}</b><small>Only if your card shows the code <b>${esc(t.code)}</b>. Not sure? Ask the City (02 279 22 11).</small></span>
      </label>`).join('');
  }
  let maillesCache = null;
  // The sector part of the card depends on the municipality (street-list sector, mailles, or nothing to choose).
  async function renderSectorBox(c = {}) {
    const muni = MUNI[form.municipality.value];
    const box = $('sectorBox');
    if (muni.kind === 'streets') {
      box.innerHTML = `<div class="field"><label>Sector (on your card)</label><select name="sector">${Object.values(window.CITY_SECTORS)
        .map((s) => `<option value="${s.id}" ${s.id === (c.sector || 'louise') ? 'selected' : ''}>${esc(s.name)}${s.nameNl !== s.name ? ' / ' + esc(s.nameNl) : ''}</option>`).join('')}</select></div>`;
      form.sector.onchange = () => renderTolerances(form.sector.value);
      renderTolerances(form.sector.value, c.tolerances || []);
      return;
    }
    $('tolBox').innerHTML = '';
    if (muni.kind !== 'mailles') { box.innerHTML = `<p class="muted small">${esc(muni.note || '')}</p>`; return; }
    box.innerHTML = '<p class="muted small"><span class="spinner" style="display:inline-block;width:12px;height:12px;border-width:2px"></span> Loading the areas from parking.brussels…</p>';
    try { maillesCache = maillesCache || await PT.maillesList(); } catch (e) { box.innerHTML = `<p class="muted small">Could not load the areas (${esc(e.message)}). Try again with internet.</p>`; return; }
    if (form.municipality.value !== muni.id) return; // changed meanwhile
    const mine = maillesCache.filter((m) => muni.prefixes.some((p) => m.zonecode === p || m.zonecode.startsWith(p + '_')))
      .sort((a, b) => String(a.namefre || a.zonecode).localeCompare(String(b.namefre || b.zonecode)));
    const lims = maillesCache.filter((m) => (muni.lim || []).includes(m.zonecode));
    const sel = new Set(c.mailles || []), selLim = new Set(c.lims || []);
    const row = (m, name, set) => `<label class="check"><input type="checkbox" name="${name}" value="${esc(m.zonecode)}" ${set.has(m.zonecode) || (mine.length === 1 && name === 'maille') ? 'checked' : ''} />
        <span><b>${esc(m.namefre || m.zonecode)}</b><small>${esc(m.zonecode)}</small></span></label>`;
    box.innerHTML = `<div class="field"><label>Areas on your card</label><p class="muted small">Tick every area (secteur / maille / zone) printed on your card.</p></div>
      <div class="stack">${mine.map((m) => row(m, 'maille', sel)).join('')}</div>
      ${lims.length ? `<div class="field"><label>Border zones</label></div><div class="stack">${lims.map((m) => row(m, 'lim', selLim)).join('')}</div>` : ''}`;
  }
  function openCard() {
    const c = state.card || {};
    form.number.value = c.number || '';
    form.plate.value = c.plate || '';
    form.municipality.value = c.municipality || 'bruxelles';
    renderSectorBox(c);
    form.from.value = c.from || '';
    form.to.value = c.to || '';
    $('scanStatus').textContent = '';
    dlg.showModal();
  }
  $('muniSelect').onchange = () => renderSectorBox({});
  $('cardBtn').onclick = openCard;
  dlg.addEventListener('close', () => {
    if (dlg.returnValue !== 'save') return;
    const first = !state.card;
    const ticked = (name) => [...form.querySelectorAll(`input[name="${name}"]:checked`)].map((i) => i.value);
    state.card = {
      number: form.number.value.trim(), plate: form.plate.value.trim().toUpperCase().replace(/[\s-]/g, ''),
      municipality: form.municipality.value, sector: form.sector?.value || null,
      from: form.from.value, to: form.to.value,
      tolerances: ticked('tol'), mailles: ticked('maille'), lims: ticked('lim'),
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
    const words = (s) => String(s).split(/\s*[-/]\s*/)[0].replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    for (const m of window.MUNICIPALITIES) {
      if (m.id !== 'bruxelles' && new RegExp(`\\b(${words(m.name)}|${words(m.nameNl)})\\b`, 'i').test(t)) { out.municipality = m.id; break; }
    }
    for (const s of Object.values(window.CITY_SECTORS)) {
      if (new RegExp(`\\b(${words(s.name)}|${words(s.nameNl)})\\b`, 'i').test(t)) { out.municipality = out.municipality || 'bruxelles'; out.sector = s.id; break; }
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
      for (const k of ['number', 'plate', 'from', 'to']) if (f[k]) form[k].value = f[k];
      if (f.municipality) form.municipality.value = f.municipality;
      if (f.municipality || f.sector || f.tolerances) {
        const keep = [...form.querySelectorAll('input[name="tol"]:checked')].map((i) => i.value);
        await renderSectorBox({ sector: f.sector, tolerances: [...keep, ...(f.tolerances || [])] });
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
    const muni = MUNI[state.card?.municipality || 'bruxelles'] || MUNI.bruxelles;
    state.muni = muni;
    state.sector = null;
    if (muni.kind === 'streets') {
      const def = window.CITY_SECTORS[state.card?.sector || 'louise'] || window.CITY_SECTORS.louise;
      // Tolerance agreements (street lists) verified for this sector: Ixelles – Louise.
      const extra = window.SECTORS[def.id];
      state.sector = PT.buildSector({
        ...def, tolerances: extra?.tolerances || [],
        sources: [...def.sources, ...((extra?.sources || []).filter((s) => !def.sources.some((d) => d.url === s.url)))],
      });
    }
    state.list = null;
    renderCardLine();
    ctx().then(() => { renderCardLine(); recheck(); refreshLayer(); });
  }

  window.__pt = { state, map, runCheck, ctx, parseCardText, refreshLayer, showFreeSpots, park, parkDetector, onParked };

  if ('serviceWorker' in navigator && location.protocol !== 'file:' && !PT.IN_APP) {
    navigator.serviceWorker.register('sw.js').catch(() => {});
  }
  renderCardLine();
  drawCar();
  syncSheetHeight();
  window.addEventListener('resize', syncSheetHeight);
  loadSector();
  // ---------- location permission ----------
  // Native helper (Android app only): see android/.../LocationAccessPlugin.java
  const LA = () => (PT.IN_APP ? window.Capacitor?.Plugins?.LocationAccess || window.Capacitor?.registerPlugin?.('LocationAccess') : null);
  async function locationStatus() {
    const la = LA();
    if (la) { try { return await la.status(); } catch { /* old app build */ } }
    try {
      const p = await navigator.permissions?.query({ name: 'geolocation' });
      if (p) return { foreground: p.state === 'granted', background: null, denied: p.state === 'denied' };
    } catch { /* not supported */ }
    return { foreground: null, background: null };
  }
  async function askAllTheTime() {
    const la = LA();
    if (!la) { toast('In your browser, location can only be allowed while the page is open.'); return; }
    const s = await locationStatus();
    if (!s.foreground) { await la.requestForeground(); startTracking(); toast('Allow location, then choose “All the time” in the next step'); return; }
    await la.requestBackground();
  }
  async function renderPermission() {
    const s = await locationStatus(), el = $('permBanner'), w = $('welcomePermState');
    const snoozed = Date.now() < (store.get('pt.permSnooze') || 0);
    let html = '';
    if (s.foreground === false) {
      html = `${ICON('locate')}<div class="grow"><b>Location is off.</b> The app needs your position to check where you park.</div>
        <div class="btns"><button type="button" class="btn btn-primary" data-perm="on">Turn on</button></div>`;
    } else if (s.background === false && !snoozed) {
      html = `${ICON('locate')}<div class="grow">For the best results, allow location <b>all the time</b>.</div>
        <div class="btns"><button type="button" class="btn btn-primary" data-perm="always">Allow</button><button type="button" class="btn btn-ghost" data-perm="later">Later</button></div>`;
    }
    el.innerHTML = html;
    el.classList.toggle('hidden', !html);
    if (w) {
      w.textContent = s.background ? '✓ Location allowed all the time.' : s.foreground ? '✓ Allowed while using the app. Tap the button to allow it all the time.' : '';
      $('welcomeAlways').classList.toggle('hidden', !!s.background || !PT.IN_APP);
    }
    syncSheetHeight();
  }
  $('permBanner').addEventListener('click', async (e) => {
    const b = e.target.closest('[data-perm]');
    if (!b) return;
    if (b.dataset.perm === 'later') { store.set('pt.permSnooze', Date.now() + 7 * 86400000); renderPermission(); return; }
    if (b.dataset.perm === 'on') {
      const la = LA();
      const s = await locationStatus();
      if (la && s.foreground === false) { await la.requestForeground(); setTimeout(() => { startTracking(); renderPermission(); }, 800); }
      else if (la) la.openSettings();
      else startTracking();
      return;
    }
    askAllTheTime();
  });
  $('welcomeAlways').onclick = askAllTheTime;
  // Back from Android's settings / permission screen: refresh and restart GPS if now allowed.
  document.addEventListener('visibilitychange', async () => {
    if (document.visibilityState !== 'visible') return;
    await renderPermission();
    const s = await locationStatus();
    if (s.foreground && !state.tracking && !state.pin) startTracking();
  });

  // Parking alerts switch (My car panel)
  $('alertsToggle').checked = alertsOn();
  $('alertsToggle').onchange = async () => {
    store.set('pt.bgAlerts', $('alertsToggle').checked);
    if ($('alertsToggle').checked) {
      try { await LNP()?.requestPermissions(); } catch { /* browser */ }
      const s = await locationStatus();
      if (s.background === false) toast('Allow location “all the time” so alerts also work in the background');
    }
    await removeWatcher();
    if (state.tracking || $('alertsToggle').checked) ensureWatcher(); // re-create with/without background mode
  };

  $('welcomeStart').onclick = () => { $('welcomeDialog').close(); openCard(); };
  // Notifications are needed for parking alerts, the timer and Android's background-location notice.
  if (PT.IN_APP && alertsOn()) setTimeout(() => { LNP()?.requestPermissions().catch(() => {}); }, 4000);
  // Ask for the position as soon as the app opens (the very first time too).
  startTracking();
  if (!state.card) setTimeout(() => { $('welcomeDialog').showModal(); renderPermission(); }, 200);
  renderPermission();
  setTimeout(renderPermission, 3000); // after the first permission answer
})();
