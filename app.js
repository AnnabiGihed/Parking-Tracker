/* Parking Tracker — is this spot free with my Brussels residential parking card?
 *
 * Every check is done LIVE:
 *   1. a fresh GPS fix (or the pin you dropped),
 *   2. the parking.brussels API (regulation_zones): which street surface the car is on, in which
 *      municipality, and the zone colour (red = residents' cards not valid),
 *   3. the City of Brussels sector list (sectors.js, verified against the official PDFs) to know
 *      which house numbers your card covers on that street,
 *   4. OpenStreetMap house-number positions to know which number you are in front of.
 * No internet, no API answer or an imprecise GPS => the app says it can't verify. It never guesses.
 */
(() => {
  'use strict';

  // ---------- settings ----------
  const IN_APP = !!window.Capacitor; // Android app: native HTTP, no CORS limits
  const PB_WFS = IN_APP
    ? 'https://ows.parking.brussels/ws/ParkingBrussels/ows'
    : '/pbapi/ws/ParkingBrussels/ows'; // browser: through tools/dev-server.mjs (the API has no CORS)
  const OVERPASS = [
    'https://overpass-api.de/api/interpreter',
    'https://overpass.private.coffee/api/interpreter',
    'https://maps.mail.ru/osm/tools/overpass/api/interpreter',
    'https://overpass.kumi.systems/api/interpreter',
  ];
  const OVERPASS_TIMEOUT_MS = 60000;
  const API_TIMEOUT_MS = 12000;
  const DATA_VERSION = 4;
  const EXPIRY_WARN_DAYS = 30;
  const MAX_GPS_ACCURACY_M = 25; // worse than this: don't decide
  const MAX_FIX_AGE_MS = 15000; // ignore cached/old GPS fixes
  const ZONE_SNAP_M = 8; // the point must be on (or this close to) a street surface of the API
  const ADDR_SNAP_M = 90; // max distance to the house number used to decide the side
  const BORDER_MARGIN_M = 12; // covered and not-covered numbers this close: "check the number"
  const RECHECK_MOVE_M = 8; // re-check after moving this much…
  const RECHECK_EVERY_MS = 30000; // …or at least this often while following
  const ZONE_NAMES = { 0: 'unregulated', 1: 'red', 2: 'orange', 3: 'grey', 4: 'green', 5: 'blue', 6: 'event', 9: 'residents only' };

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
  const clock = (t = new Date()) => t.toLocaleTimeString('fr-BE', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  function withTimeout(promise, ms, what) {
    let timer;
    return Promise.race([promise, new Promise((_, ko) => { timer = setTimeout(() => ko(new Error(what + ' timed out')), ms); })])
      .finally(() => clearTimeout(timer));
  }

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

  // ---------- geometry ----------
  const R = 6371000, RAD = Math.PI / 180, EARTH_MERC = 6378137;
  function dist(aLat, aLon, bLat, bLon) {
    const x = (bLon - aLon) * RAD * Math.cos(((aLat + bLat) / 2) * RAD);
    const y = (bLat - aLat) * RAD;
    return Math.sqrt(x * x + y * y) * R;
  }
  const toMercator = (lat, lon) => [lon * RAD * EARTH_MERC, Math.log(Math.tan(Math.PI / 4 + (lat * RAD) / 2)) * EARTH_MERC];
  function segDist(px, py, ax, ay, bx, by) {
    const dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
    const t = len ? Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / len)) : 0;
    return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
  }
  function inRing(px, py, ring) {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const [xi, yi] = ring[i], [xj, yj] = ring[j];
      if ((yi > py) !== (yj > py) && px < ((xj - xi) * (py - yi)) / (yj - yi) + xi) inside = !inside;
    }
    return inside;
  }
  // Distance (in projection units) from a point to a (Multi)Polygon; 0 when inside.
  function polygonDist(px, py, geom) {
    const polys = geom.type === 'Polygon' ? [geom.coordinates] : geom.type === 'MultiPolygon' ? geom.coordinates : [];
    let best = Infinity;
    for (const rings of polys) {
      if (inRing(px, py, rings[0]) && !rings.slice(1).some((h) => inRing(px, py, h))) return 0;
      for (const ring of rings) {
        for (let i = 1; i < ring.length; i++) best = Math.min(best, segDist(px, py, ring[i - 1][0], ring[i - 1][1], ring[i][0], ring[i][1]));
      }
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
  // One street table: st.odd / st.even = City of Brussels sector rules (territory BRUXELLES),
  // st.tol = [{code, label, odd, even}] tolerance rules (territory of the neighbouring municipality).
  function buildSector(def) {
    const streets = [], lookup = new Map();
    const get = (fr, nl) => {
      const k = normalize(fr);
      if (lookup.has(k)) return streets[lookup.get(k)];
      const st = { i: streets.length, fr, nl, odd: parseRule('hors'), even: parseRule('hors'), inSector: false, tol: [] };
      streets.push(st);
      lookup.set(k, st.i); lookup.set(normalize(nl), st.i);
      return st;
    };
    for (const [fr, nl, odd, even] of def.streets) {
      const st = get(fr, nl);
      st.odd = parseRule(odd); st.even = parseRule(even); st.inSector = true;
    }
    for (const t of def.tolerances || []) {
      for (const [fr, nl, odd, even] of t.streets) {
        get(fr, nl).tol.push({ code: t.code, label: t.label, commune: t.commune, odd: parseRule(odd || 'hors'), even: parseRule(even || 'hors') });
      }
    }
    return { ...def, streets, lookup };
  }
  const activeCodes = () => new Set(state.card?.tolerances || []);
  function streetIndex(sector, ...names) {
    for (const n of names) for (const p of nameParts(n)) if (sector.lookup.has(p)) return sector.lookup.get(p);
    return -1;
  }
  // Rule sets that apply on a street, depending on the municipality the car is in (from the API).
  function ruleSetsFor(st, commune, codes = activeCodes()) {
    const c = normalize(commune);
    const sets = [];
    if (c === 'bruxelles' && st.inSector) sets.push({ label: 'your sector', odd: st.odd, even: st.even });
    for (const t of st.tol) {
      if (codes.has(t.code) && c === normalize(t.commune)) sets.push({ label: t.label, odd: t.odd, even: t.even });
    }
    return sets;
  }
  const setsCover = (sets, house) => sets.find((r) => ruleCovers(house.n % 2 ? r.odd : r.even, house)) || null;

  // ---------- live API: parking.brussels regulation zones ----------
  async function liveZone(lat, lon) {
    const d = 0.0003; // ~30 m search box
    const url = `${PB_WFS}?service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json`
      + `&typeNames=ParkingBrussels:regulation_zones&srsName=EPSG:3857`
      + `&bbox=${(lat - d).toFixed(6)},${(lon - d).toFixed(6)},${(lat + d).toFixed(6)},${(lon + d).toFixed(6)},urn:ogc:def:crs:EPSG::4326`;
    const json = await withTimeout(fetch(url).then((r) => {
      if (!r.ok) throw new Error('parking.brussels HTTP ' + r.status);
      return r.json();
    }), API_TIMEOUT_MS, 'parking.brussels');
    if (!json || !Array.isArray(json.features)) throw new Error('parking.brussels: unexpected answer');
    const [px, py] = toMercator(lat, lon);
    const scale = Math.cos(lat * RAD); // mercator metres -> real metres
    let best = null;
    for (const f of json.features) {
      if (!f.geometry || f.properties?.disabled_on) continue;
      const m = polygonDist(px, py, f.geometry) * scale;
      if (!best || m < best.m) best = { m, ...f.properties };
    }
    return best; // { m, pn_name_fr, pn_name_du, mu_name_fr, typreg, ZoneId1, … } or null
  }

  // ---------- OpenStreetMap house numbers (positions only) ----------
  function looseRegex(name) {
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
      try {
        const res = await withTimeout(fetch(url, {
          method: 'POST', body: 'data=' + encodeURIComponent(query),
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        }), OVERPASS_TIMEOUT_MS, 'OpenStreetMap');
        if (!res.ok) throw new Error('Overpass ' + res.status);
        const json = await res.json();
        if (!json?.elements?.length) throw new Error(json?.remark || 'Overpass: empty answer');
        return json;
      } catch (e) { lastErr = e; }
    }
    throw lastErr;
  }
  async function fetchSectorData(sector) {
    const [s, w, n, e] = sector.area;
    const names = sector.streets.flatMap((st) => [st.fr, st.nl]).map(looseRegex);
    const q = `[out:json][timeout:90];
nwr["addr:housenumber"]["addr:street"~"^(${names.join('|')})( |$)",i](${s},${w},${n},${e});
out center tags;`;
    const json = await overpass(q);
    const addrs = [];
    for (const el of json.elements) {
      const t = el.tags || {};
      const idx = streetIndex(sector, t['addr:street']);
      const lat = el.lat ?? el.center?.lat, lon = el.lon ?? el.center?.lon;
      if (idx < 0 || lat == null) continue;
      for (const hn of String(t['addr:housenumber']).split(/[;,]/)) {
        if (parseHouse(hn)) addrs.push({ i: idx, h: hn.trim(), a: +lat.toFixed(6), o: +lon.toFixed(6) });
      }
    }
    return { v: DATA_VERSION, sector: sector.id, ts: Date.now(), addrs };
  }
  function prepare(sector, data) {
    for (const a of data.addrs) { a.house = parseHouse(a.h); a.odd = a.house.n % 2 === 1; }
    data.byStreet = new Map();
    for (const a of data.addrs) {
      if (!data.byStreet.has(a.i)) data.byStreet.set(a.i, []);
      data.byStreet.get(a.i).push(a);
    }
    evaluate(sector, data);
    return data;
  }
  // Map hints (green dots): addresses covered by the sector or an active tolerance. The live check decides.
  function evaluate(sector = state.sector, data = state.data) {
    if (!sector || !data) return;
    const codes = activeCodes();
    for (const a of data.addrs) {
      const st = sector.streets[a.i];
      const sets = [...ruleSetsFor(st, 'Bruxelles', codes), ...st.tol.flatMap((t) => ruleSetsFor(st, t.commune, codes))];
      a.ok = !!setsCover(sets, a.house);
    }
    data.free = data.addrs.filter((a) => a.ok);
  }

  // ---------- the decision ----------
  function cardState(card) {
    if (!card) return { valid: false, why: 'No parking card set up yet — tap 🪪.' };
    const t = todayISO();
    if (card.from && t < card.from) return { valid: false, why: `Your card is only valid from ${fmtDate(card.from)}.` };
    if (card.to && t > card.to) return { valid: false, why: `Your card expired on ${fmtDate(card.to)}.` };
    return { valid: true };
  }
  const result = (status, title, detail, extra = {}) => ({ status, title, detail, ...extra });
  const cantVerify = (detail, extra) => result('unknown', "CAN'T VERIFY", detail, extra);

  async function decide(lat, lon, accuracy) {
    const cs = cardState(state.card);
    if (!cs.valid) return result('pay', 'PAY', cs.why);
    if (!navigator.onLine) return cantVerify('No internet connection. The app only answers with live data.');
    if (accuracy != null && accuracy > MAX_GPS_ACCURACY_M) {
      return cantVerify(`GPS is not precise enough yet (±${Math.round(accuracy)} m, need ±${MAX_GPS_ACCURACY_M} m). Waiting for a better fix…`);
    }
    if (!state.data) return cantVerify('Still downloading house-number positions…');

    let zone;
    try { zone = await liveZone(lat, lon); } catch (e) { return cantVerify(`Could not reach parking.brussels (${esc(e.message)}).`); }
    const snap = ZONE_SNAP_M + Math.min(accuracy || 0, 15);
    if (!zone || zone.m > snap) return cantVerify('This point is not on a street with parking rules. Put the pin on the street, where you park.');

    const color = ZONE_NAMES[zone.typreg] ?? 'unknown';
    const extra = { zone, street: zone.pn_name_fr };
    const where = `<b>${esc(zone.pn_name_fr)}</b> (${esc(zone.mu_name_fr)}, ${esc(color)} zone)`;

    if (zone.typreg === 0) return result('free', 'FREE ✓', `${where}: no parking rules here, free for everyone.`, extra);
    if (zone.typreg === 1) return result('pay', 'PAY', `${where}: red zone, residents' cards are never valid here.`, extra);
    if (zone.typreg === 6) return cantVerify(`${where}: event zone, the normal rules don't apply. Follow the signs.`, extra);
    if (![2, 3, 4, 5, 9].includes(zone.typreg)) return cantVerify(`${where}: zone type unknown to the app. Follow the signs.`, extra);

    const notCovered = zone.typreg === 5
      ? (txt) => result('unknown', 'PARKING DISC', `${txt} Blue zone: put your blue parking disc, time is limited.`, extra)
      : (txt) => result('pay', 'PAY', `${txt} Pay at the meter or with a parking app.`, extra);

    const sector = state.sector;
    const idx = streetIndex(sector, zone.pn_name_fr, zone.pn_name_du);
    const st = idx >= 0 ? sector.streets[idx] : null;
    const sets = st ? ruleSetsFor(st, zone.mu_name_fr) : [];
    if (!sets.length) {
      const hint = st && st.tol.some((t) => normalize(t.commune) === normalize(zone.mu_name_fr)) ? ' (only with a tolerance code your card does not have)' : '';
      return notCovered(`${where} is not in your sector ${esc(sector.name)}${hint}.`);
    }
    const ruleText = sets.map((r) => `${esc(r.label)}: odd <b>${esc(r.odd.text)}</b>, even <b>${esc(r.even.text)}</b>`).join('; ');
    if (sets.some((r) => r.odd.all && r.even.all)) return result('free', 'FREE ✓', `${where}: the whole street is covered by your card (${esc(sets[0].label)}).`, extra);

    // Which house number is the car in front of? Nearest address on that street.
    const addrs = state.data.byStreet.get(st.i) || [];
    let best = null, bd = Infinity;
    for (const a of addrs) { const m = dist(lat, lon, a.a, a.o); if (m < bd) { bd = m; best = a; } }
    if (!best || bd > ADDR_SNAP_M) return cantVerify(`${where} is only partly covered (${ruleText}). Look at the house number.`, extra);

    const margin = Math.max(BORDER_MARGIN_M, accuracy || 0);
    const close = addrs.filter((a) => dist(lat, lon, a.a, a.o) <= bd + margin);
    const yes = close.filter((a) => setsCover(sets, a.house)), no = close.filter((a) => !setsCover(sets, a.house));
    if (yes.length && no.length) {
      const list = (xs) => [...new Set(xs.map((a) => a.h))].slice(0, 4).join(', ');
      return result('unknown', 'CHECK THE NUMBER', `You are at a limit on ${where}. Free in front of <b>${esc(list(yes))}</b>, pay in front of <b>${esc(list(no))}</b>. ${ruleText}.`, extra);
    }
    const cov = setsCover(sets, best.house);
    const front = `in front of <b>${esc(st.fr)} ${esc(best.h)}</b> (${best.odd ? 'odd' : 'even'} side, ${esc(zone.mu_name_fr)}, ${esc(color)} zone)`;
    return cov
      ? result('free', 'FREE ✓', `You are ${front}: covered by your card (${esc(cov.label)}).`, extra)
      : notCovered(`You are ${front}: not covered. ${ruleText}.`);
  }

  // Nearest covered spots (from the map hints), each confirmed live before it is shown.
  async function confirmedFreeSpot(lat, lon) {
    if (!state.data || !cardState(state.card).valid) return null;
    const byStreet = new Map();
    for (const a of state.data.free) {
      const m = dist(lat, lon, a.a, a.o);
      if (!byStreet.has(a.i) || m < byStreet.get(a.i).m) byStreet.set(a.i, { m, a });
    }
    const candidates = [...byStreet.values()].sort((x, y) => x.m - y.m).slice(0, 4);
    for (const c of candidates) {
      try {
        const z = await liveZone(c.a.a, c.a.o); // the address point is the building: allow up to 25 m to the street
        if (!z || z.m > 25 || [0, 1, 6].includes(z.typreg)) continue;
        const st = state.sector.streets[c.a.i];
        if (streetIndex(state.sector, z.pn_name_fr, z.pn_name_du) !== st.i) continue;
        if (!setsCover(ruleSetsFor(st, z.mu_name_fr), c.a.house)) continue;
        return { ...c, zone: z };
      } catch { return null; }
    }
    return null;
  }

  // ---------- UI ----------
  const state = {
    card: store.get('pt.card'), sector: null, data: null,
    tracking: false, watchId: null, me: null, pin: null, lastStatus: null,
    lastCheck: null, checkSeq: 0, busy: false,
  };

  const map = L.map('map', { zoomControl: false, preferCanvas: true }).setView([50.8285, 4.3640], 15);
  L.control.zoom({ position: 'topleft' }).addTo(map);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> · zones: parking.brussels',
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

  function renderStatus(r, source, accuracy) {
    const box = $('status');
    box.className = 'status ' + r.status;
    const who = source === 'me' ? '📍 Your position: ' : '📌 Pin: ';
    box.innerHTML = `<div class="status-title">${r.title}</div><div class="status-detail">${who}${r.detail}</div>`;
    const live = [];
    if (r.zone) live.push('🟢 live parking.brussels');
    live.push('checked ' + clock());
    if (source === 'me' && accuracy != null) live.push(`GPS ±${Math.round(accuracy)} m`);
    $('liveLine').textContent = live.join(' · ');
    syncSheetHeight();
  }
  function renderPending(source) {
    $('liveLine').textContent = `⏳ checking live (${source === 'me' ? 'your position' : 'pin'})…`;
  }

  async function showFreeSpot(lat, lon, onlyIfStale) {
    const near = $('nearest');
    if (onlyIfStale && !near.classList.contains('hidden')) return;
    near.innerHTML = '🔎 Looking for the nearest covered spot (confirming live)…';
    near.classList.remove('hidden'); syncSheetHeight();
    const best = await confirmedFreeSpot(lat, lon);
    if (!best) { near.innerHTML = 'No confirmed covered spot nearby right now.'; syncSheetHeight(); return; }
    const st = state.sector.streets[best.a.i];
    near.innerHTML = `🟢 Nearest covered spot (confirmed live): <b>${esc(st.fr)} ${esc(best.a.h)}</b> — ${Math.round(best.m)} m away${navLinks(best.a.a, best.a.o)}`;
    showTarget(best.a);
    syncSheetHeight();
  }
  function hideFreeSpot() {
    $('nearest').classList.add('hidden');
    if (targetMarker) { map.removeLayer(targetMarker); targetMarker = null; }
    syncSheetHeight();
  }

  // Runs a live check; only the latest one is shown.
  async function runCheck(lat, lon, source, accuracy) {
    const seq = ++state.checkSeq;
    state.lastCheck = { lat, lon, t: Date.now(), source, accuracy };
    renderPending(source);
    let r;
    try { r = await decide(lat, lon, accuracy); } catch (e) { r = cantVerify('Unexpected error: ' + esc(e.message)); }
    if (seq !== state.checkSeq) return r;
    renderStatus(r, source, accuracy);
    if (r.status === 'free') hideFreeSpot();
    else if (r.zone || r.status === 'pay') showFreeSpot(lat, lon, source === 'me');
    if (source === 'me' && r.status !== 'unknown') {
      if (state.lastStatus && r.status !== state.lastStatus) {
        toast(r.status === 'free' ? '🟢 You are now in a spot covered by your card' : '🔴 Not covered here');
        if (navigator.vibrate) navigator.vibrate(r.status === 'free' ? [80, 60, 80] : [300]);
      }
      state.lastStatus = r.status;
    }
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
      L.circleMarker([a.a, a.o], { radius: 3, color: '#16a34a', weight: 1, fillColor: '#22c55e', fillOpacity: 0.9, interactive: false })
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
    runCheck(latlng.lat, latlng.lng, 'pin');
  }
  map.on('click', (e) => { if (state.tracking) stopTracking(); setPin(e.latlng); });

  // --- live position ---
  function onPosition(pos) {
    const { latitude: lat, longitude: lon, accuracy } = pos.coords;
    if (Date.now() - pos.timestamp > MAX_FIX_AGE_MS) return; // stale fix
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
    const moved = !lc || lc.source !== 'me' || dist(lat, lon, lc.lat, lc.lon) >= RECHECK_MOVE_M;
    const old = !lc || Date.now() - lc.t >= RECHECK_EVERY_MS;
    const better = lc && lc.accuracy > MAX_GPS_ACCURACY_M && accuracy <= MAX_GPS_ACCURACY_M;
    if (moved || old || better) runCheck(lat, lon, 'me', accuracy);
  }
  function startTracking() {
    if (!navigator.geolocation) { toast('Location is not available on this device.'); return; }
    state.tracking = true; state.lastStatus = null; state.lastCheck = null;
    $('trackBtn').classList.add('active');
    if (pinMarker) { map.removeLayer(pinMarker); pinMarker = null; state.pin = null; }
    $('liveLine').textContent = '⏳ waiting for GPS…';
    state.watchId = navigator.geolocation.watchPosition(onPosition, (err) => {
      renderStatus(cantVerify('No GPS position: ' + esc(err.message) + '. Allow location for this app.'), 'me');
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
  // Re-check periodically even when standing still, so the answer is never older than 30 s.
  setInterval(() => {
    if (state.tracking && state.me && Date.now() - (state.lastCheck?.t || 0) >= RECHECK_EVERY_MS) {
      if (Date.now() - state.me.t > MAX_FIX_AGE_MS) renderStatus(cantVerify('Lost the GPS signal. Waiting for a fresh position…'), 'me');
      else runCheck(state.me.lat, state.me.lon, 'me', state.me.accuracy);
    }
  }, 5000);

  window.addEventListener('offline', () => renderStatus(cantVerify('Internet connection lost. The app only answers with live data.'), state.tracking ? 'me' : 'pin'));
  window.addEventListener('online', () => {
    toast('Back online');
    if (state.tracking && state.me) runCheck(state.me.lat, state.me.lon, 'me', state.me.accuracy);
    else if (state.pin) runCheck(state.pin.lat, state.pin.lng, 'pin');
  });

  $('findBtn').onclick = () => {
    const from = state.tracking && state.me ? state.me : state.pin ? { lat: state.pin.lat, lon: state.pin.lng } : state.me;
    if (!from) { toast('Press 📍 or tap the map first.'); return; }
    showFreeSpot(from.lat, from.lon, false).then(() => {
      if (targetMarker) map.fitBounds(L.latLngBounds([[from.lat, from.lon], targetMarker.getLatLng()]).pad(0.4));
    });
  };

  // --- address search ---
  $('searchForm').onsubmit = async (e) => {
    e.preventDefault();
    const q = $('searchInput').value.trim();
    if (!q) return;
    try {
      const url = `https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=be&viewbox=4.24,50.92,4.48,50.76&bounded=1&q=${encodeURIComponent(q)}`;
      const res = await (await withTimeout(fetch(url, { headers: { 'Accept-Language': 'fr' } }), API_TIMEOUT_MS, 'search')).json();
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
    const first = !state.card;
    const sectorChanged = state.card?.sector !== form.sector.value;
    state.card = {
      number: form.number.value.trim(), plate: form.plate.value.trim().toUpperCase().replace(/[\s-]/g, ''),
      sector: form.sector.value, from: form.from.value, to: form.to.value,
      tolerances: [...form.querySelectorAll('input[name="tol"]:checked')].map((i) => i.value),
    };
    store.set('pt.card', state.card);
    renderCardLine();
    if (sectorChanged) loadSector();
    else { evaluate(); drawFreeLayer(); recheck(); }
    if (first && !state.tracking) startTracking();
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

  // --- data loading (house-number positions; refreshed at every start) ---
  function recheck() {
    renderCardLine();
    if (state.tracking && state.me) runCheck(state.me.lat, state.me.lon, 'me', state.me.accuracy);
    else if (state.pin) runCheck(state.pin.lat, state.pin.lng, 'pin');
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
    // House positions don't change, so the last download is used while a fresh one loads.
    if (cached && cached.v === DATA_VERSION && cached.sector === id) {
      state.data = prepare(state.sector, cached); drawFreeLayer(); recheck();
    }
    $('loading').classList.remove('hidden'); syncSheetHeight();
    try {
      const fresh = await fetchSectorData(state.sector);
      store.set(key, fresh);
      state.data = prepare(state.sector, fresh);
      drawFreeLayer();
      recheck();
    } catch (err) {
      if (!state.data) toast('Could not download house numbers (' + err.message + '). Check your connection and reopen the app.', 8000);
    } finally {
      $('loading').classList.add('hidden'); syncSheetHeight();
    }
  }

  // expose for debugging/tests
  window.__pt = { state, decide, liveZone, evaluate, parseRule, ruleCovers, parseHouse, parseCardText, normalize, runCheck };

  // The Android app (Capacitor) already ships the files, so the offline worker is only for the web version.
  if ('serviceWorker' in navigator && location.protocol !== 'file:' && !IN_APP) {
    navigator.serviceWorker.register('sw.js').catch(() => {});
  }

  renderCardLine();
  syncSheetHeight();
  window.addEventListener('resize', syncSheetHeight);
  loadSector();
  if (!state.card) setTimeout(openCard, 300);
  else startTracking(); // live position from the start
})();
