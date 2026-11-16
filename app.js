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

  // ---------- live map layers ----------
  const LAYERS = {
    streets: { minZoom: LAYER_MIN_ZOOM, group: surfacesLayer, on: true },
    spaces: { minZoom: 18, group: L.layerGroup().addTo(map), on: true },
    works: { minZoom: 15, group: L.layerGroup().addTo(map), on: true },
    carparks: { minZoom: 14, group: L.layerGroup().addTo(map), on: false },
    chargers: { minZoom: 15, group: L.layerGroup().addTo(map), on: false },
  };
  const savedLayers = store.get('pt.layers') || {};
  for (const [k, v] of Object.entries(savedLayers)) if (LAYERS[k]) LAYERS[k].on = !!v;
  const poi = (emoji) => L.divIcon({ className: '', html: `<div class="poi-icon">${emoji}</div>`, iconSize: [22, 22], iconAnchor: [11, 11] });
  const DRAW = {
    async streets(box) {
      const surfaces = await PT.liveArea(await ctx(), box);
      return (g) => {
        for (const s of surfaces) {
          const c = KIND_STYLE[s.kind].color;
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
    if (!state.sector || !state.card) return;
    if (!navigator.onLine) { for (const l of Object.values(LAYERS)) l.group.clearLayers(); st.textContent = 'offline: no live layers'; return; }
    const b = map.getBounds().pad(0.1), box = [b.getSouth(), b.getWest(), b.getNorth(), b.getEast()], z = map.getZoom();
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
    st.textContent = z < LAYER_MIN_ZOOM ? 'zoom in to see street colours' : errors.length ? 'some layers failed: ' + errors.join('; ') : 'updated ' + clock();
  }
  map.on('moveend', scheduleLayer);
  setInterval(refreshLayer, 120000); // keep layers fresh

  // Layers panel
  $('layersBtn').onclick = () => $('layersPanel').classList.toggle('hidden');
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

  // Payment happens in the operators' own apps; these buttons open them.
  const PAY_APPS = [['4411', 'https://www.4411.io'], ['EasyPark', 'https://www.easypark.com'], ['Seety', 'https://seety.co']];
  function renderStatus(r, source, accuracy) {
    const box = $('status');
    box.className = 'status ' + r.status;
    const notes = (r.notes || []).length ? `<ul class="notes">${r.notes.map((n) => `<li>${n}</li>`).join('')}</ul>` : '';
    const pay = r.status === 'pay' && r.zone && !r.reserved
      ? `<div class="paylinks">Pay with: ${PAY_APPS.map(([n, u]) => `<a href="${u}" target="_blank" rel="noopener">${n}</a>`).join('')}</div>` : '';
    box.innerHTML = `<div class="status-title">${r.title}</div><div class="status-detail">${source === 'me' ? '📍 Your position: ' : '📌 Pin: '}${r.detail}</div>${notes}${pay}`;
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

  // ---------- my car + parking timer ----------
  let carMarker = null;
  const LN = () => window.Capacitor?.Plugins?.LocalNotifications;
  function drawCar() {
    const car = store.get('pt.car');
    if (carMarker) { map.removeLayer(carMarker); carMarker = null; }
    if (car) {
      carMarker = L.marker([car.lat, car.lon], { icon: L.divIcon({ className: '', html: '<div class="car-icon">🚗</div>', iconSize: [28, 28], iconAnchor: [14, 14] }) })
        .bindPopup(`My car — parked ${new Date(car.t).toLocaleString('fr-BE', { dateStyle: 'short', timeStyle: 'short' })}`).addTo(map);
    }
    renderTimer();
  }
  function renderCarDialog() {
    const car = store.get('pt.car'), timer = store.get('pt.timer');
    $('carInfo').innerHTML = car
      ? `Parked ${new Date(car.t).toLocaleString('fr-BE', { dateStyle: 'short', timeStyle: 'short' })}${car.status ? ` — the app said <b>${esc(car.status)}</b>` : ''}${car.where ? `<br>${car.where}` : ''}`
      : 'No car saved. Save it where you park, to find it back and set a reminder.';
    $('carActions').innerHTML = car
      ? `<a class="btnlink primary" target="_blank" rel="noopener" href="https://www.google.com/maps/dir/?api=1&destination=${car.lat.toFixed(6)},${car.lon.toFixed(6)}&travelmode=walking">🚶 Walk to my car</a>
         <button type="button" id="carShow">Show on the map</button>
         <button type="button" id="carHere">Update: my car is here now</button>
         <button type="button" id="carClear">I left (clear)</button>`
      : '<button type="button" id="carHere" class="primary">📍 My car is parked here</button>';
    $('timerStop').disabled = !timer;
    const bind = (id, fn) => { const el = $(id); if (el) el.onclick = fn; };
    bind('carHere', saveCarHere);
    bind('carShow', () => { const c = store.get('pt.car'); $('carDialog').close(); map.setView([c.lat, c.lon], 18); carMarker?.openPopup(); });
    bind('carClear', () => { store.set('pt.car', null); stopTimer(); drawCar(); renderCarDialog(); });
  }
  function saveCarHere() {
    const at = state.tracking && state.me ? { lat: state.me.lat, lon: state.me.lon } : state.pin ? { lat: state.pin.lat, lon: state.pin.lng } : state.me;
    if (!at) { toast('No position yet: press 📍 or drop a pin.'); return; }
    const box = $('status');
    store.set('pt.car', { ...at, t: Date.now(), status: box.querySelector('.status-title')?.textContent || '', where: box.querySelector('.status-detail b')?.outerHTML || '' });
    drawCar(); renderCarDialog(); toast('🚗 Car position saved');
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
    toast(`⏱️ Reminder set: ${minutes} min`);
  }
  async function stopTimer() {
    store.set('pt.timer', null);
    (state.webTimers || []).forEach(clearTimeout);
    try { await LN()?.cancel({ notifications: [{ id: 4101 }, { id: 4102 }] }); } catch { /* nothing scheduled */ }
    renderTimer(); renderCarDialog();
  }
  function renderTimer() {
    const t = store.get('pt.timer'), el = $('timerLine');
    if (!t) { el.classList.add('hidden'); syncSheetHeight(); return; }
    const left = t.end - Date.now();
    el.classList.remove('hidden');
    el.innerHTML = left > 0
      ? `⏱️ Parking time left: <b>${Math.floor(left / 3600000)}h${String(Math.floor((left % 3600000) / 60000)).padStart(2, '0')}</b> (until ${new Date(t.end).toLocaleTimeString('fr-BE', { hour: '2-digit', minute: '2-digit' })})`
      : '⏱️ <b>Your parking time is over.</b>';
    syncSheetHeight();
  }
  setInterval(renderTimer, 30000);
  $('carBtn').onclick = () => { renderCarDialog(); $('carDialog').showModal(); };
  $('timerStart').onclick = startTimer;
  $('timerStop').onclick = stopTimer;

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
  drawCar();
  syncSheetHeight();
  window.addEventListener('resize', syncSheetHeight);
  loadSector();
  if (!state.card) setTimeout(openCard, 300);
  else startTracking();
})();
