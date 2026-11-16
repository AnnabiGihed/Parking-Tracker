/* Parking Tracker — checks whether a spot is covered by a Brussels residential parking card. */
(() => {
  'use strict';

  const OVERPASS = [
    'https://overpass-api.de/api/interpreter',
    'https://overpass.private.coffee/api/interpreter',
    'https://maps.mail.ru/osm/tools/overpass/api/interpreter',
    'https://overpass.kumi.systems/api/interpreter',
  ];
  const OVERPASS_TIMEOUT_MS = 60000;
  const DATA_VERSION = 3;
  const DATA_MAX_AGE = 30 * 24 * 3600 * 1000; // refresh street data monthly
  const ROAD_SNAP_M = 35; // a point must be this close to a named street
  const ADDR_SNAP_M = 90; // max distance to the house number used to decide the side
  const EXPIRY_WARN_DAYS = 30;
  const BORDER_MARGIN_M = 12; // typical phone GPS error; closer than this to a limit = "check the number"

  // ---------- small helpers ----------
  const $ = (id) => document.getElementById(id);
  const store = {
    get(k) { try { return JSON.parse(localStorage.getItem(k)); } catch { return null; } },
    set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); return true; } catch { return false; } },
  };
  const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const todayISO = () => { const d = new Date(); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10); };
  const fmtDate = (iso) => (iso ? iso.split('-').reverse().join('/') : '?');
  const daysBetween = (a, b) => Math.round((new Date(b) - new Date(a)) / 86400000);

  function normalize(s) {
    return String(s || '')
      .normalize('NFD').replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[’'`\-.]/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }
  // "Avenue Louise - Louizalaan" -> ["avenue louise", "louizalaan"]
  const nameParts = (s) => String(s || '').split(/\s+-\s+|;/).map(normalize).filter(Boolean);

  // ---------- geometry (local equirectangular projection, fine at city scale) ----------
  const R = 6371000, RAD = Math.PI / 180;
  function dist(aLat, aLon, bLat, bLon) {
    const x = (bLon - aLon) * RAD * Math.cos(((aLat + bLat) / 2) * RAD);
    const y = (bLat - aLat) * RAD;
    return Math.sqrt(x * x + y * y) * R;
  }
  function distToPolyline(lat, lon, pts) {
    const k = Math.cos(lat * RAD) * RAD * R, kl = RAD * R;
    let best = Infinity;
    for (let i = 1; i < pts.length; i++) {
      const ax = (pts[i - 1][1] - lon) * k, ay = (pts[i - 1][0] - lat) * kl;
      const bx = (pts[i][1] - lon) * k, by = (pts[i][0] - lat) * kl;
      const dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
      let t = len ? -(ax * dx + ay * dy) / len : 0;
      t = Math.max(0, Math.min(1, t));
      const px = ax + t * dx, py = ay + t * dy;
      best = Math.min(best, Math.sqrt(px * px + py * py));
    }
    return best;
  }

  // ---------- house-number rules ----------
  function parseHouse(s) {
    const m = String(s || '').trim().match(/^(\d+)\s*([a-z]?)/i);
    return m ? { n: +m[1], l: m[2].toLowerCase() } : null;
  }
  const letterRank = (l) => (l ? l.charCodeAt(0) - 96 : 0);
  const keyLow = (h) => h.n * 100 + letterRank(h.l);
  const keyHigh = (h) => h.n * 100 + (h.l ? letterRank(h.l) : 99); // "74" also covers 74A, 74B…

  function parseRule(raw) {
    const s = String(raw || '').trim().toLowerCase();
    if (!s) return { all: true, text: 'whole side' };
    if (s.startsWith('hors')) return { none: true, text: 'not covered' };
    const ranges = s.split(/\s+et\s+|,/).map((part) => {
      part = part.trim();
      let m = part.match(/^(\S+)\s*(?:à|a)\s*fin/);
      if (m) return [keyLow(parseHouse(m[1])), Infinity];
      m = part.match(/^(\S+?)\s*-\s*(\S+)$/);
      if (m) return [keyLow(parseHouse(m[1])), keyHigh(parseHouse(m[2]))];
      const h = parseHouse(part);
      return h ? [keyLow(h), keyHigh(h)] : null;
    }).filter(Boolean);
    return { ranges, text: String(raw).replace(/à fin/g, '→ end') };
  }
  function ruleCovers(rule, house) {
    if (rule.all) return true;
    if (rule.none) return false;
    const k = keyLow(house);
    return rule.ranges.some(([a, b]) => k >= a && k <= b);
  }

  // ---------- sector model ----------
  // Merges the sector list and its tolerance lists into one street table:
  // st.odd / st.even = sector rules, st.tol = [{code, odd, even}] tolerance rules.
  function buildSector(def) {
    const streets = [], lookup = new Map();
    const get = (fr, nl) => {
      const k = normalize(fr);
      if (lookup.has(k)) return streets[lookup.get(k)];
      const st = { i: streets.length, fr, nl, odd: parseRule('hors'), even: parseRule('hors'), tol: [] };
      streets.push(st);
      lookup.set(k, st.i); lookup.set(normalize(nl), st.i);
      return st;
    };
    for (const [fr, nl, odd, even] of def.streets) {
      const st = get(fr, nl);
      st.odd = parseRule(odd); st.even = parseRule(even);
    }
    for (const t of def.tolerances || []) {
      for (const [fr, nl, odd, even] of t.streets) {
        get(fr, nl).tol.push({ code: t.code, label: t.label, odd: parseRule(odd || 'hors'), even: parseRule(even || 'hors') });
      }
    }
    return { ...def, streets, lookup };
  }
  const activeCodes = () => new Set(state.card?.tolerances || []);
  // Returns 'sector', a tolerance code, or null.
  function coverage(st, house, codes = activeCodes()) {
    const odd = house.n % 2 === 1;
    if (ruleCovers(odd ? st.odd : st.even, house)) return 'sector';
    for (const t of st.tol) if (codes.has(t.code) && ruleCovers(odd ? t.odd : t.even, house)) return t.code;
    return null;
  }
  function matchStreet(sector, osmName) {
    for (const p of nameParts(osmName)) if (sector.lookup.has(p)) return sector.lookup.get(p);
    return -1;
  }

  // ---------- OpenStreetMap data (streets + house numbers) ----------
  function looseRegex(name) {
    // Accent/punctuation tolerant pattern; results are re-checked exactly on the client.
    let out = '';
    for (const ch of name) {
      if (/[aeiouyc]/i.test(ch) || /[^\x00-\x7f]/.test(ch)) out += '.{1,2}';
      else if (/[\s'’\-]/.test(ch)) out += '.{1,3}';
      else out += ch;
    }
    return out;
  }
  async function overpass(query) {
    let lastErr;
    for (const url of OVERPASS) {
      const ctrl = new AbortController();
      const timer = setTimeout(() => ctrl.abort(), OVERPASS_TIMEOUT_MS);
      try {
        const res = await fetch(url, { method: 'POST', body: 'data=' + encodeURIComponent(query), headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, signal: ctrl.signal });
        if (!res.ok) throw new Error('Overpass ' + res.status);
        const json = await res.json();
        if (json.remark && /error|timed out/i.test(json.remark) && !json.elements?.length) throw new Error(json.remark);
        return json;
      } catch (e) { lastErr = e; } finally { clearTimeout(timer); }
    }
    throw lastErr;
  }
  async function fetchSectorData(sector) {
    const [s, w, n, e] = sector.area;
    const bbox = `${s},${w},${n},${e}`;
    const names = sector.streets.flatMap((st) => [st.fr, st.nl]).map(looseRegex);
    const re = `^(${names.join('|')})( |$)`;
    const q = `[out:json][timeout:90];
nwr["addr:housenumber"]["addr:street"~"${re}",i](${bbox});
out center tags;
way["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|service|road)(_link)?$"]["name"](${bbox});
out tags geom;`;
    const json = await overpass(q);
    const roads = [], addrs = [];
    for (const el of json.elements) {
      const t = el.tags || {};
      if (t.highway && el.geometry) {
        roads.push({
          d: t['name:fr'] || String(t.name).split(/\s+-\s+/)[0],
          n: [...new Set([t.name, t['name:fr'], t['name:nl']].flatMap(nameParts))],
          p: el.geometry.map((g) => [+g.lat.toFixed(6), +g.lon.toFixed(6)]),
        });
      } else if (t['addr:housenumber']) {
        const idx = matchStreet(sector, t['addr:street']);
        const lat = el.lat ?? el.center?.lat, lon = el.lon ?? el.center?.lon;
        if (idx < 0 || lat == null) continue;
        for (const hn of String(t['addr:housenumber']).split(/[;,]/)) {
          if (parseHouse(hn)) addrs.push({ i: idx, h: hn.trim(), a: +lat.toFixed(6), o: +lon.toFixed(6) });
        }
      }
    }
    return { v: DATA_VERSION, sector: sector.id, ts: Date.now(), roads, addrs };
  }

  // ---------- classification ----------
  function prepare(sector, data) {
    for (const r of data.roads) r.idx = r.n.reduce((acc, p) => (acc >= 0 ? acc : sector.lookup.get(p) ?? -1), -1);
    for (const a of data.addrs) {
      a.house = parseHouse(a.h);
      a.odd = a.house.n % 2 === 1;
    }
    data.byStreet = new Map();
    for (const a of data.addrs) {
      if (!data.byStreet.has(a.i)) data.byStreet.set(a.i, []);
      data.byStreet.get(a.i).push(a);
    }
    evaluate(sector, data);
    return data;
  }
  // (Re)computes which addresses are covered; depends on the card's tolerance codes.
  function evaluate(sector = state.sector, data = state.data) {
    if (!sector || !data) return;
    const codes = activeCodes();
    for (const a of data.addrs) {
      a.cov = coverage(sector.streets[a.i], a.house, codes);
      a.ok = !!a.cov;
    }
    data.free = data.addrs.filter((a) => a.ok);
  }
  const tolLabel = (code) => state.sector?.tolerances?.find((t) => t.code === code)?.label || code;

  function cardState(card) {
    if (!card) return { valid: false, why: 'No parking card set up yet.' };
    const t = todayISO();
    if (card.from && t < card.from) return { valid: false, why: `Your card is only valid from ${fmtDate(card.from)}.` };
    if (card.to && t > card.to) return { valid: false, why: `Your card expired on ${fmtDate(card.to)}.` };
    return { valid: true };
  }

  function classify(lat, lon) {
    const sector = state.sector, data = state.data;
    const cs = cardState(state.card);
    if (!sector || !data) return { status: 'unknown', title: 'Loading…', detail: 'Street data for your sector is still loading.' };
    if (!cs.valid) return { status: 'pay', title: 'PAY', detail: cs.why };

    const [s, w, n, e] = sector.area;
    if (lat < s || lat > n || lon < w || lon > e) {
      return { status: 'pay', title: 'PAY', detail: `Outside your sector (${esc(sector.name)}). Your residential card does not apply here — pay at the meter / app unless the signs say parking is free.` };
    }

    let road = null, rd = Infinity;
    for (const r of data.roads) {
      const d = distToPolyline(lat, lon, r.p);
      if (d < rd) { rd = d; road = r; }
    }
    if (!road || rd > ROAD_SNAP_M) return { status: 'unknown', title: 'No street here', detail: 'Drop the pin on the street where you want to park.' };

    const roadName = esc(road.d);
    if (road.idx < 0) {
      return { status: 'pay', title: 'PAY', detail: `<b>${roadName}</b> is not in your sector ${esc(sector.name)}. Pay at the meter / app.`, street: road.d };
    }
    const st = sector.streets[road.idx];
    const codes = activeCodes();
    const tols = st.tol.filter((t) => codes.has(t.code));
    if (st.odd.all && st.even.all) return { status: 'free', title: 'FREE ✓', detail: `<b>${esc(st.fr)}</b> — the whole street is in your sector.`, street: st.fr };
    if (st.odd.none && st.even.none && !tols.length) {
      return { status: 'pay', title: 'PAY', detail: `<b>${esc(st.fr)}</b> is not in your sector ${esc(sector.name)}${st.tol.length ? ' (only with a tolerance code your card does not have)' : ''}. Pay at the meter / app.`, street: st.fr };
    }

    // Find the nearest house number on this street to know the side (odd/even) and the number.
    let best = null, bd = Infinity;
    for (const a of data.byStreet.get(st.i) || []) {
      const d = dist(lat, lon, a.a, a.o);
      if (d < bd) { bd = d; best = a; }
    }
    let rules = `Covered here: odd <b>${esc(st.odd.text)}</b>, even <b>${esc(st.even.text)}</b>`;
    for (const t of tols) rules += `; ${esc(t.label)}: odd <b>${esc(t.odd.text)}</b>, even <b>${esc(t.even.text)}</b>`;
    rules += '.';
    if (!best || bd > ADDR_SNAP_M) {
      return { status: 'unknown', title: 'CHECK THE NUMBER', detail: `<b>${esc(st.fr)}</b> is only partly in your sector. ${rules} Look at the nearest house number.` };
    }
    const where = `<b>${esc(st.fr)} ${esc(best.h)}</b> (${best.odd ? 'odd' : 'even'} side)`;
    // Safety margin: if covered and not-covered numbers are both close by (a range limit, or the
    // middle of the road between an odd and an even side), don't guess — ask to check the number.
    const close = (data.byStreet.get(st.i) || []).filter((a) => dist(lat, lon, a.a, a.o) <= bd + BORDER_MARGIN_M);
    if (close.some((a) => a.ok) && close.some((a) => !a.ok)) {
      const yes = [...new Set(close.filter((a) => a.ok).map((a) => a.h))].slice(0, 4).join(', ');
      const no = [...new Set(close.filter((a) => !a.ok).map((a) => a.h))].slice(0, 4).join(', ');
      return { status: 'unknown', title: 'CHECK THE NUMBER', detail: `You are at a limit on <b>${esc(st.fr)}</b>. Free in front of <b>${esc(yes)}</b>, pay in front of <b>${esc(no)}</b>. ${rules}`, street: st.fr };
    }
    return best.ok
      ? { status: 'free', title: 'FREE ✓', detail: `In front of ${where} — covered by your card${best.cov !== 'sector' ? ` (${esc(tolLabel(best.cov))})` : ''}.`, street: st.fr }
      : { status: 'pay', title: 'PAY', detail: `In front of ${where} — not in your sector. ${rules}`, street: st.fr };
  }

  function nearestFree(lat, lon, count = 3) {
    if (!state.data || !cardState(state.card).valid) return [];
    const seen = new Map();
    for (const a of state.data.free) {
      const d = dist(lat, lon, a.a, a.o);
      const prev = seen.get(a.i);
      if (!prev || d < prev.d) seen.set(a.i, { d, a });
    }
    return [...seen.values()].sort((x, y) => x.d - y.d).slice(0, count);
  }

  // ---------- UI ----------
  const state = { card: store.get('pt.card'), sector: null, data: null, tracking: false, watchId: null, me: null, pin: null, lastStatus: null };

  const map = L.map('map', { zoomControl: false, preferCanvas: true }).setView([50.8285, 4.3640], 15);
  L.control.zoom({ position: 'topleft' }).addTo(map);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
  }).addTo(map);
  const freeLayer = L.layerGroup();
  let meMarker = null, meAcc = null, pinMarker = null, targetMarker = null;

  function toast(msg, ms = 3500) {
    const t = $('toast');
    t.textContent = msg; t.classList.remove('hidden');
    clearTimeout(toast.timer); toast.timer = setTimeout(() => t.classList.add('hidden'), ms);
  }
  function syncSheetHeight() {
    document.documentElement.style.setProperty('--sheet-h', $('sheet').offsetHeight + 'px');
  }

  function renderCardLine() {
    const c = state.card, el = $('cardLine');
    if (!c) { el.innerHTML = '<span class="warn">No card yet — tap 🪪 to add yours.</span>'; return; }
    const cs = cardState(c);
    const left = c.to ? daysBetween(todayISO(), c.to) : null;
    let warn = '';
    if (!cs.valid) warn = `<span class="warn">${esc(cs.why)}</span>`;
    else if (left !== null && left <= EXPIRY_WARN_DAYS) {
      warn = `<span class="warn">Expires in ${left} day(s) — <a href="mailto:circulation.dem@brucity.be?subject=${encodeURIComponent('Renouvellement carte riverain ' + (c.number || ''))}">renew</a></span>`;
    }
    const tol = (c.tolerances || []).length ? ' + tolerance' : '';
    el.innerHTML = `<span>🪪 ${esc(c.plate || 'plate?')} · sector <b>${esc(state.sector?.name || c.sector)}</b>${tol} · until ${fmtDate(c.to)}</span>${warn}`;
  }

  function navLinks(lat, lon) {
    return `<div class="btns">
      <a class="nav waze" target="_blank" rel="noopener" href="https://waze.com/ul?ll=${lat},${lon}&navigate=yes">Waze</a>
      <a class="nav gmaps" target="_blank" rel="noopener" href="https://www.google.com/maps/dir/?api=1&destination=${lat},${lon}&travelmode=driving">Google Maps</a>
    </div>`;
  }

  function showResult(lat, lon, source) {
    const r = classify(lat, lon);
    const box = $('status');
    box.className = 'status ' + r.status;
    box.innerHTML = `<div class="status-title">${r.title}</div><div class="status-detail">${source === 'me' ? '📍 Your position: ' : '📌 Pin: '}${r.detail}</div>`;

    const near = $('nearest');
    if (r.status !== 'free') {
      const opts = nearestFree(lat, lon);
      if (opts.length) {
        const best = opts[0];
        const st = state.sector.streets[best.a.i];
        const others = opts.slice(1).map((o) => `${esc(state.sector.streets[o.a.i].fr)} (${Math.round(o.d)} m)`).join(', ');
        near.innerHTML = `🟢 Nearest free spot: <b>${esc(st.fr)} ${esc(best.a.h)}</b> — ${Math.round(best.d)} m away${others ? `<br><small>Also: ${others}</small>` : ''}${navLinks(best.a.a, best.a.o)}`;
        near.classList.remove('hidden');
        showTarget(best.a);
      } else near.classList.add('hidden');
    } else {
      near.classList.add('hidden');
      if (targetMarker) { map.removeLayer(targetMarker); targetMarker = null; }
    }
    syncSheetHeight();
    return r;
  }

  function showTarget(a) {
    if (targetMarker) map.removeLayer(targetMarker);
    targetMarker = L.circleMarker([a.a, a.o], { radius: 10, color: '#16a34a', weight: 3, fillOpacity: 0.2 }).addTo(map);
  }

  function drawFreeLayer() {
    freeLayer.clearLayers();
    if (!state.data) return;
    for (const a of state.data.free) {
      L.circleMarker([a.a, a.o], { radius: 3, color: '#16a34a', weight: 1, fillColor: '#22c55e', fillOpacity: 0.9 })
        .bindTooltip(`${state.sector.streets[a.i].fr} ${a.h}`)
        .addTo(freeLayer);
    }
    updateLayerVisibility();
  }
  function updateLayerVisibility() {
    if (map.getZoom() >= 15) freeLayer.addTo(map); else map.removeLayer(freeLayer);
  }
  map.on('zoomend', updateLayerVisibility);

  // --- pin ---
  function setPin(latlng) {
    state.pin = latlng;
    if (!pinMarker) {
      pinMarker = L.marker(latlng, { draggable: true }).addTo(map);
      pinMarker.on('dragend', () => setPin(pinMarker.getLatLng()));
    } else pinMarker.setLatLng(latlng);
    showResult(latlng.lat, latlng.lng, 'pin');
  }
  map.on('click', (e) => { if (state.tracking) stopTracking(); setPin(e.latlng); });

  // --- live position ---
  function onPosition(pos) {
    const { latitude: lat, longitude: lon, accuracy } = pos.coords;
    state.me = { lat, lon };
    if (!meMarker) {
      meMarker = L.marker([lat, lon], { icon: L.divIcon({ className: '', html: '<div class="me-dot"></div>', iconSize: [18, 18] }), interactive: false }).addTo(map);
      meAcc = L.circle([lat, lon], { radius: accuracy, color: '#2563eb', weight: 1, fillOpacity: 0.08, interactive: false }).addTo(map);
      map.setView([lat, lon], 17);
    } else {
      meMarker.setLatLng([lat, lon]); meAcc.setLatLng([lat, lon]).setRadius(accuracy);
      if (state.tracking) map.panTo([lat, lon], { animate: true });
    }
    if (!state.tracking) return;
    const r = showResult(lat, lon, 'me');
    if (state.lastStatus && r.status !== state.lastStatus && r.status !== 'unknown') {
      toast(r.status === 'free' ? '🟢 You are now in a FREE spot for your card' : '🔴 Paid parking here');
      if (navigator.vibrate) navigator.vibrate(r.status === 'free' ? [80, 60, 80] : [300]);
    }
    if (r.status !== 'unknown') state.lastStatus = r.status;
  }
  function startTracking() {
    if (!navigator.geolocation) { toast('Location is not available on this device.'); return; }
    state.tracking = true; state.lastStatus = null;
    $('trackBtn').classList.add('active');
    if (pinMarker) { map.removeLayer(pinMarker); pinMarker = null; }
    state.watchId = navigator.geolocation.watchPosition(onPosition, (err) => {
      toast('Location error: ' + err.message); stopTracking();
    }, { enableHighAccuracy: true, maximumAge: 5000, timeout: 20000 });
  }
  function stopTracking() {
    state.tracking = false;
    $('trackBtn').classList.remove('active');
    if (state.watchId != null) navigator.geolocation.clearWatch(state.watchId);
    state.watchId = null;
  }
  $('trackBtn').onclick = () => (state.tracking ? stopTracking() : startTracking());

  $('findBtn').onclick = () => {
    const from = state.tracking && state.me ? state.me : state.pin ? { lat: state.pin.lat, lon: state.pin.lng } : state.me;
    if (!from) { toast('Press 📍 or tap the map first.'); return; }
    const opts = nearestFree(from.lat, from.lon, 1);
    if (!opts.length) { toast(cardState(state.card).valid ? 'Street data not loaded yet.' : 'Your card is not valid.'); return; }
    const a = opts[0].a;
    showTarget(a);
    map.fitBounds(L.latLngBounds([[from.lat, from.lon], [a.a, a.o]]).pad(0.4));
    const near = $('nearest');
    near.innerHTML = `🟢 Nearest free spot: <b>${esc(state.sector.streets[a.i].fr)} ${esc(a.h)}</b> — ${Math.round(opts[0].d)} m away${navLinks(a.a, a.o)}`;
    near.classList.remove('hidden');
    syncSheetHeight();
  };

  // --- address search ---
  $('searchForm').onsubmit = async (e) => {
    e.preventDefault();
    const q = $('searchInput').value.trim();
    if (!q) return;
    try {
      const url = `https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=be&viewbox=4.24,50.92,4.48,50.76&bounded=1&q=${encodeURIComponent(q)}`;
      const res = await (await fetch(url, { headers: { 'Accept-Language': 'fr' } })).json();
      if (!res.length) { toast('Address not found.'); return; }
      const ll = L.latLng(+res[0].lat, +res[0].lon);
      if (state.tracking) stopTracking();
      map.setView(ll, 18);
      setPin(ll);
      $('searchInput').blur();
    } catch { toast('Search failed — check your connection.'); }
  };

  // --- card dialog ---
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
    const sectorChanged = state.card?.sector !== form.sector.value;
    state.card = {
      number: form.number.value.trim(), plate: form.plate.value.trim().toUpperCase().replace(/[\s-]/g, ''),
      sector: form.sector.value, from: form.from.value, to: form.to.value,
      tolerances: [...form.querySelectorAll('input[name="tol"]:checked')].map((i) => i.value),
    };
    store.set('pt.card', state.card);
    renderCardLine();
    if (sectorChanged) loadSector();
    else { evaluate(); drawFreeLayer(); refreshCurrent(); }
  });

  // --- OCR of a card photo (Tesseract.js, loaded only when used) ---
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
    // Tolerance codes, e.g. BXL_LIM_IXL_LOUISE (OCR may turn "_" into spaces or dashes).
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

  // --- data loading ---
  function refreshCurrent() {
    renderCardLine();
    if (state.tracking && state.me) showResult(state.me.lat, state.me.lon, 'me');
    else if (state.pin) showResult(state.pin.lat, state.pin.lng, 'pin');
    syncSheetHeight();
  }
  async function loadSector() {
    const id = state.card?.sector || 'louise';
    const def = window.SECTORS[id];
    if (!def) return;
    state.sector = buildSector(def);
    state.data = null;
    renderCardLine();
    const key = `pt.data.${id}`;
    const cached = store.get(key);
    const usable = cached && cached.v === DATA_VERSION && cached.sector === id;
    if (usable) { state.data = prepare(state.sector, cached); drawFreeLayer(); refreshCurrent(); }
    if (usable && Date.now() - cached.ts < DATA_MAX_AGE) return;

    $('loading').classList.remove('hidden'); syncSheetHeight();
    try {
      const fresh = await fetchSectorData(state.sector);
      store.set(key, fresh);
      state.data = prepare(state.sector, fresh);
      drawFreeLayer();
      refreshCurrent();
      toast(`Sector ${state.sector.name}: ${state.data.free.length} covered addresses loaded.`);
    } catch (err) {
      if (!state.data) toast('Could not load street data (' + err.message + '). Retry later.', 6000);
    } finally {
      $('loading').classList.add('hidden'); syncSheetHeight();
    }
  }

  // expose for debugging/tests
  window.__pt = { state, classify, evaluate, parseRule, ruleCovers, parseHouse, parseCardText, normalize };

  // The Android app (Capacitor) already ships the files, so the offline worker is only for the web version.
  if ('serviceWorker' in navigator && location.protocol !== 'file:' && !window.Capacitor) {
    navigator.serviceWorker.register('sw.js').catch(() => {});
  }

  renderCardLine();
  syncSheetHeight();
  window.addEventListener('resize', syncSheetHeight);
  loadSector();
  if (!state.card) setTimeout(openCard, 300);
})();
