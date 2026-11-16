/* Parking Tracker — live data and decisions (no UI).
 *
 * Nothing here answers from stored data. Each decision uses, at the moment it is made:
 *   - parking.brussels API (regulation_zones): the street surface, its municipality and zone colour;
 *   - Brussels Region UrbIS address points (official house numbers) around the spot;
 *   - the City of Brussels sector list (sectors.js), but only after the official documents it was
 *     verified against have been checked live as unchanged.
 * If any of these can't be obtained, the answer is "can't verify".
 */
(() => {
  'use strict';

  // ---------- settings ----------
  const IN_APP = !!window.Capacitor; // Android app: native HTTP, no CORS limits
  const PB_WFS = IN_APP ? 'https://ows.parking.brussels/ws/ParkingBrussels/ows' : '/pbapi/ws/ParkingBrussels/ows';
  const BXL_SITE = 'https://www.bruxelles.be';
  const bxlUrl = (u) => (IN_APP ? u : u.replace(BXL_SITE, '/bxl')); // browser: through tools/dev-server.mjs
  const URBIS_WFS = 'https://data.mobility.brussels/geoserver/bm_urbis/wfs'; // allows browser calls (CORS *)
  const BM_WFS = 'https://data.mobility.brussels/geoserver/ows'; // Brussels Mobility (roadworks, car parks, charging)
  // Parking rules per zone code (paid days/hours, prices, max stay, fine). No CORS: browser goes through the dev server.
  const RULES_API = (IN_APP ? 'https://api.mobility.brussels' : '/bmapi') + '/datasets/v1/parking/collections/regulations_rules/items/';
  // Reserved parking spaces (parking.brussels on_street "typres"). Time-limited zones are allowed for a short time.
  const RESERVED = {
    1: '🚚 Delivery space', 2: '🚛 Trucks only', 3: '🚌 Bus only', 5: '♿ Disabled (PMR) space', 6: '🚕 Taxi rank',
    8: '🏛️ Diplomatic (CD) space', 9: '🚓 Police only', 10: '🏍️ Motorbikes only', 11: '🚲 Bicycles only', 12: '👋 Kiss & ride',
    13: '🚚 Delivery zone', 14: '👋 Kiss & ride zone', 16: '⏱️ 30-minute zone', 17: '⏱️ 60-minute zone', 18: '🛍️ Shop & go zone',
    19: '⚡ EV charging zone', 20: '🚗 Car-sharing zone', 21: '🚌 Coach drop zone', 22: '🚌 Coach waiting zone', 23: '🚌 Coach overnight zone',
    24: '🚛 Truck zone', 25: '🚛 Zone E1', 26: '⛔ Other reserved space',
  };
  const TIME_LIMITED = new Set([16, 17, 18]);
  const C = {
    API_TIMEOUT_MS: 12000,
    CORNER_AMBIGUITY_M: 4, // address about as close to two different streets: left out
    LIST_RECHECK_MS: 6 * 3600 * 1000,
    MAX_GPS_ACCURACY_M: 25,
    ZONE_SNAP_M: 8,
    ADDR_TO_SURFACE_M: 35, // a house number belongs to a street surface if it is this close
    BORDER_MARGIN_M: 12,
  };
  const ZONE_NAMES = { 0: 'unregulated', 1: 'red', 2: 'orange', 3: 'grey', 4: 'green', 5: 'blue', 6: 'event', 9: 'residents only' };

  // ---------- helpers ----------
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const todayISO = () => { const d = new Date(); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10); };
  const fmtDate = (iso) => (iso ? iso.split('-').reverse().join('/') : '?');
  function withTimeout(promise, ms, what) {
    let timer;
    return Promise.race([promise, new Promise((_, ko) => { timer = setTimeout(() => ko(new Error(what + ' timed out')), ms); })])
      .finally(() => clearTimeout(timer));
  }
  function normalize(s) {
    return String(s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
      .replace(/[’'`\-.]/g, ' ').replace(/\s+/g, ' ').trim();
  }
  const titleCase = (s) => String(s || '').toLowerCase().replace(/(^|[\s-])\S/g, (m) => m.toUpperCase());
  const nameParts = (s) => String(s || '').split(/\s+-\s+|;/).map(normalize).filter(Boolean);

  // ---------- geometry ----------
  const R = 6371000, RAD = Math.PI / 180, EM = 6378137;
  function dist(aLat, aLon, bLat, bLon) {
    const x = (bLon - aLon) * RAD * Math.cos(((aLat + bLat) / 2) * RAD);
    const y = (bLat - aLat) * RAD;
    return Math.sqrt(x * x + y * y) * R;
  }
  const toMerc = (lat, lon) => [lon * RAD * EM, Math.log(Math.tan(Math.PI / 4 + (lat * RAD) / 2)) * EM];
  const fromMerc = (x, y) => [(2 * Math.atan(Math.exp(y / EM)) - Math.PI / 2) / RAD, x / EM / RAD];
  const polysOf = (g) => (g.type === 'Polygon' ? [g.coordinates] : g.type === 'MultiPolygon' ? g.coordinates : []);
  function inRing(px, py, ring) {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const [xi, yi] = ring[i], [xj, yj] = ring[j];
      if ((yi > py) !== (yj > py) && px < ((xj - xi) * (py - yi)) / (yj - yi) + xi) inside = !inside;
    }
    return inside;
  }
  // Closest point of a (Multi)Polygon to (lat, lon): { m: metres (0 inside), lat, lon }.
  function closestOnSurface(lat, lon, geom) {
    const [px, py] = toMerc(lat, lon);
    let best = { d: Infinity, x: px, y: py };
    for (const rings of polysOf(geom)) {
      if (inRing(px, py, rings[0]) && !rings.slice(1).some((h) => inRing(px, py, h))) return { m: 0, lat, lon };
      for (const ring of rings) {
        for (let i = 1; i < ring.length; i++) {
          const [ax, ay] = ring[i - 1], [bx, by] = ring[i];
          const dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
          const t = len ? Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / len)) : 0;
          const x = ax + t * dx, y = ay + t * dy, d = Math.hypot(px - x, py - y);
          if (d < best.d) best = { d, x, y };
        }
      }
    }
    const [la, lo] = fromMerc(best.x, best.y);
    return { m: best.d * Math.cos(lat * RAD), lat: la, lon: lo };
  }
  // Box grown by ~45 m so that addresses near its edge still see the streets they face.
  const pad = ([s, w, n, e], d = 0.0004) => [s - d, w - d * 1.6, n + d, e + d * 1.6];
  const surfaceLatLngs = (geom) => polysOf(geom).map((rings) => rings.map((ring) => ring.map(([x, y]) => fromMerc(x, y))));

  // ---------- house-number rules ----------
  function parseHouse(s) {
    const m = String(s || '').trim().match(/^(\d+)\s*([a-z]?)/i);
    return m ? { n: +m[1], l: m[2].toLowerCase() } : null;
  }
  const letterRank = (l) => (l ? l.charCodeAt(0) - 96 : 0);
  const keyLow = (h) => h.n * 100 + letterRank(h.l);
  const keyHigh = (h) => h.n * 100 + (h.l ? letterRank(h.l) : 99);
  function parseRule(raw) {
    const s = String(raw || '').trim().toLowerCase();
    if (!s) return { all: true, text: 'whole side' };
    if (s.startsWith('hors')) return { none: true, text: 'not covered' };
    if (s.startsWith('check:')) return { check: true, none: true, text: String(raw).slice(6).trim() };
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
  function streetIndex(sector, ...names) {
    for (const n of names) for (const p of nameParts(n)) if (sector.lookup.has(p)) return sector.lookup.get(p);
    return -1;
  }
  function ruleSetsFor(st, commune, codes) {
    const c = normalize(commune), sets = [];
    if (c === 'bruxelles' && st.inSector) sets.push({ label: 'your sector', odd: st.odd, even: st.even });
    for (const t of st.tol) if (codes.has(t.code) && c === normalize(t.commune)) sets.push({ label: t.label, odd: t.odd, even: t.even });
    return sets;
  }
  const setsCover = (sets, house) => sets.find((r) => ruleCovers(house.n % 2 ? r.odd : r.even, house)) || null;

  // ---------- live source 1: official City documents unchanged? ----------
  async function sha256hex(buf) {
    const h = await crypto.subtle.digest('SHA-256', buf);
    return [...new Uint8Array(h)].map((b) => b.toString(16).padStart(2, '0')).join('');
  }
  // Downloads a file as raw bytes. In the Android app, Capacitor's patched fetch() reads non-JSON bodies
  // as text (which corrupts a PDF), so the native HTTP plugin is asked for base64 instead.
  async function downloadBytes(url) {
    const ch = IN_APP ? window.Capacitor?.Plugins?.CapacitorHttp : null;
    if (ch) {
      const r = await withTimeout(ch.request({ url, method: 'GET', responseType: 'arraybuffer', headers: {} }), 30000, 'bruxelles.be');
      if (r.status >= 400) throw new Error('bruxelles.be HTTP ' + r.status);
      const bin = atob(String(r.data || '').replace(/\s/g, ''));
      const bytes = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      return bytes;
    }
    const g = await withTimeout(fetch(url, { cache: 'no-store' }), 30000, 'bruxelles.be');
    if (!g.ok) throw new Error('bruxelles.be HTTP ' + g.status);
    return new Uint8Array(await g.arrayBuffer());
  }
  // Header values can be reshaped on the way (native HTTP layer, proxies, compression), so they are only a
  // quick hint. A document only counts as CHANGED when its content (SHA-256 of the PDF) differs.
  const normEtag = (v) => String(v || '').replace(/^W\//i, '').replace(/"/g, '').trim().toLowerCase();
  const normDate = (v) => { const t = Date.parse(String(v || '')); return Number.isNaN(t) ? null : t; };
  async function checkOfficialList(sector) {
    const seen = [];
    try {
      for (const s of sector.sources || []) {
        let headerSaysSame = false, lm = null;
        try {
          const r = await withTimeout(fetch(bxlUrl(s.url), { method: 'HEAD', cache: 'no-store' }), C.API_TIMEOUT_MS, 'bruxelles.be');
          const etag = r.headers.get('etag'); lm = r.headers.get('last-modified');
          seen.push(`${r.status} ${lm || '-'} ${etag || '-'}`);
          const dateSame = normDate(lm) != null && normDate(lm) === normDate(s.lastModified);
          const etagSame = !!etag && normEtag(etag) === normEtag(s.etag);
          headerSaysSame = r.ok && (dateSame || etagSame);
        } catch (e) { seen.push('HEAD failed: ' + e.message); }
        if (headerSaysSame) continue;
        // Not sure from the headers: compare the document itself.
        const bytes = await downloadBytes(bxlUrl(s.url));
        const isPdf = bytes.length > 4 && bytes[0] === 0x25 && bytes[1] === 0x50 && bytes[2] === 0x44 && bytes[3] === 0x46; // "%PDF"
        if (!isPdf) throw new Error('the City sent something that is not the PDF');
        if ((await sha256hex(bytes)) !== s.sha256) return { status: 'changed', doc: s.url, date: lm, t: Date.now(), seen };
      }
      return { status: 'ok', t: Date.now(), seen };
    } catch (e) {
      return { status: 'unverified', why: e.message, t: Date.now(), seen };
    }
  }

  // ---------- live source 2: parking.brussels street surfaces ----------
  async function liveZones([s, w, n, e]) {
    const url = `${PB_WFS}?service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json`
      + `&typeNames=ParkingBrussels:regulation_zones&srsName=EPSG:3857&count=3000`
      + `&bbox=${s.toFixed(6)},${w.toFixed(6)},${n.toFixed(6)},${e.toFixed(6)},urn:ogc:def:crs:EPSG::4326`;
    const json = await withTimeout(fetch(url, { cache: 'no-store' }).then((r) => {
      if (!r.ok) throw new Error('parking.brussels HTTP ' + r.status);
      return r.json();
    }), C.API_TIMEOUT_MS, 'parking.brussels');
    if (!json || !Array.isArray(json.features)) throw new Error('parking.brussels: unexpected answer');
    // Tunnels and viaducts run under/over other streets: nobody parks there, and their surface would
    // otherwise hide the street above or below (e.g. Tunnel Bailli under Avenue Louise).
    const notParkable = (p) => /^(tunnel|viaduc|tr[ée]mie|pont)\b/i.test(String(p?.pn_name_fr || '').trim());
    return json.features.filter((f) => f.geometry && !f.properties?.disabled_on && !notParkable(f.properties)).map((f) => {
      let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
      for (const rings of polysOf(f.geometry)) for (const [x, y] of rings[0]) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
      return { id: f.properties.id, p: f.properties, g: f.geometry, bb: [x0, y0, x1, y1] };
    });
  }

  // ---------- live source 3: official house numbers (Brussels Region UrbIS address points) ----------
  // Each address point sits on the façade of its building. It is given the street of the
  // parking.brussels street surface it faces (nearest surface). Corner addresses that are about
  // as close to another street, and numbers that don't fit their neighbours, are left out.
  async function liveAddresses(sector, box, zones) {
    const [s, w, n, e] = box;
    const url = `${URBIS_WFS}?service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json`
      + `&typeNames=bm_urbis:urbadm_adpt&srsName=EPSG:3857&propertyName=adrn,geom&count=5000`
      + `&bbox=${s.toFixed(6)},${w.toFixed(6)},${n.toFixed(6)},${e.toFixed(6)},urn:ogc:def:crs:EPSG::4326`;
    const json = await withTimeout(fetch(url, { cache: 'no-store' }).then((r) => {
      if (!r.ok) throw new Error('UrbIS HTTP ' + r.status);
      return r.json();
    }), C.API_TIMEOUT_MS, 'UrbIS addresses');
    if (!json || !Array.isArray(json.features)) throw new Error('UrbIS: unexpected answer');
    const all = [];
    for (const f of json.features) {
      const house = parseHouse(f.properties?.adrn);
      if (!house || f.geometry?.type !== 'Point') continue;
      const [px, py] = f.geometry.coordinates;
      const [lat, lon] = fromMerc(px, py);
      const k = Math.cos(lat * RAD);
      let best = null, bm = Infinity, other = Infinity;
      for (const z of zones) {
        const [x0, y0, x1, y1] = z.bb; // quick lower bound: skip surfaces that can't be among the 2 nearest
        const lb = Math.hypot(Math.max(x0 - px, 0, px - x1), Math.max(y0 - py, 0, py - y1)) * k;
        if (lb > other && lb > bm) continue;
        const m = closestOnSurface(lat, lon, z.g).m;
        if (m < bm) {
          if (best && normalize(best.p.pn_name_fr) !== normalize(z.p.pn_name_fr)) other = Math.min(other, bm);
          bm = m; best = z;
        } else if (best && normalize(best.p.pn_name_fr) !== normalize(z.p.pn_name_fr)) other = Math.min(other, m);
      }
      if (!best || bm > C.ADDR_TO_SURFACE_M || other - bm < C.CORNER_AMBIGUITY_M) continue;
      all.push({ h: String(f.properties.adrn).trim(), house, a: lat, o: lon, street: normalize(best.p.pn_name_fr),
        commune: normalize(best.p.mu_name_fr), surfaceId: best.id, i: streetIndex(sector, best.p.pn_name_fr, best.p.pn_name_du) });
    }
    // Outliers: a number far from all of its 3 nearest neighbours on the same street is probably misplaced.
    return all.filter((a) => {
      const nb = all.filter((b) => b !== a && b.street === a.street)
        .map((b) => ({ b, m: dist(a.a, a.o, b.a, b.o) })).sort((x, y) => x.m - y.m).slice(0, 3);
      return !nb.length || nb.some(({ b }) => Math.abs(b.house.n - a.house.n) <= 20);
    });
  }

  // ---------- live source 4: individual parking spaces, roadworks, car parks, charging ----------
  async function wfs(base, layer, [s, w, n, e], what, extra = '') {
    const url = `${base}?service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json&typeNames=${layer}`
      + `&srsName=EPSG:3857&count=5000${extra}&bbox=${s.toFixed(6)},${w.toFixed(6)},${n.toFixed(6)},${e.toFixed(6)},urn:ogc:def:crs:EPSG::4326`;
    const json = await withTimeout(fetch(url, { cache: 'no-store' }).then((r) => {
      if (!r.ok) throw new Error(what + ' HTTP ' + r.status);
      return r.json();
    }), C.API_TIMEOUT_MS, what);
    if (!json || !Array.isArray(json.features)) throw new Error(what + ': unexpected answer');
    return json.features.filter((f) => f.geometry);
  }
  const points = (g) => (g.type === 'Point' ? [g.coordinates] : g.type === 'MultiPoint' ? g.coordinates : []).map(([x, y]) => fromMerc(x, y));

  // Every on-street parking space, with its reservation type (0 = ordinary space).
  async function liveSpaces(box) {
    const out = [];
    for (const f of await wfs(PB_WFS, 'ParkingBrussels:on_street', box, 'parking.brussels spaces')) {
      const p = f.properties || {};
      for (const [lat, lon] of points(f.geometry)) out.push({ lat, lon, typres: +p.typres || 0, typreg: p.typreg, label: RESERVED[+p.typres] || null });
    }
    return out;
  }
  const parseDMY = (s) => { const m = String(s || '').match(/^(\d{2})\/(\d{2})\/(\d{4})/); return m ? `${m[3]}-${m[2]}-${m[1]}` : null; };
  // Current roadworks (Osiris) and traffic events (Brussels Mobility), still running today.
  async function liveWorks(box) {
    const today = todayISO(), now = new Date().toISOString();
    const [sites, events] = await Promise.all([
      wfs(BM_WFS, 'bm_osiris:chantiers_actu', box, 'roadworks'),
      wfs(BM_WFS, 'bm_traffic:events', box, 'traffic events'),
    ]);
    const works = [];
    for (const f of sites) {
      const p = f.properties || {}, start = parseDMY(p.str_start_date), end = parseDMY(p.str_end_date);
      if ((end && end < today) || (start && start > today)) continue;
      works.push({ kind: 'works', g: f.geometry, title: p.nature_fr || 'Roadworks', where: p.road_impl_fr || p.nom_fr || '',
        from: start, to: end, by: p.pilote_fr || '', link: p.link_fr || '' });
    }
    for (const f of events) {
      const p = f.properties || {};
      if (p.is_active === false || (p.end_time && p.end_time < now) || (p.start_time && p.start_time > now)) continue;
      works.push({ kind: 'event', g: f.geometry, title: (p.type_fr || 'Event').trim(), where: p.location_fr || '',
        from: p.start_time?.slice(0, 10), to: p.end_time?.slice(0, 10), by: p.consequences_fr || '', link: '' });
    }
    return works;
  }
  async function liveCarParks(box) {
    return (await wfs(BM_WFS, 'bm_parking:parkingpublic', box, 'car parks')).filter((f) => f.properties?.active !== false).map((f) => {
      const p = f.properties, [lat, lon] = points(f.geometry)[0];
      return { lat, lon, name: p.name_fr, address: [p.road_fr, p.housenr].filter(Boolean).join(' '), capacity: p.capacity,
        disabled: p.disabledcapacity, operator: p.operator_fr, phone: p.contact_phone, maxheight: p.maxheight > 0 ? p.maxheight : null };
    });
  }
  async function liveChargers(box) {
    return (await wfs(BM_WFS, 'bm_equipment:evse', box, 'charging points')).map((f) => {
      const p = f.properties, [lat, lon] = points(f.geometry)[0];
      return { lat, lon, operator: p.operator_name, points: p.charging_points, power: p.power, standard: p.connector_standard, type: p.connector_type };
    });
  }
  // Is the point inside / near a running roadworks or event?
  function worksAt(works, lat, lon, radius = 25) {
    return works.filter((w) => {
      if (w.g.type === 'Point' || w.g.type === 'MultiPoint') return points(w.g).some(([a, o]) => dist(lat, lon, a, o) <= radius);
      return closestOnSurface(lat, lon, w.g).m <= 5;
    });
  }

  // ---------- live source 5: parking rules (hours and prices) ----------
  // Returns Map(code -> rule) of the rules in force now. With `code`, only that zone's rule.
  async function liveRules(code) {
    const url = `${RULES_API}?f=json&limit=500${code ? '&code=' + encodeURIComponent(code) : ''}`;
    const json = await withTimeout(fetch(url, { cache: 'no-store' }).then((r) => {
      if (!r.ok) throw new Error('parking rules HTTP ' + r.status);
      return r.json();
    }), C.API_TIMEOUT_MS, 'parking rules');
    if (!json || !Array.isArray(json.features)) throw new Error('parking rules: unexpected answer');
    const now = new Date().toISOString(), out = new Map();
    for (const f of json.features) {
      const p = f.properties || {};
      if (!p.code || p.active === 0 || (p.disabled_on && p.disabled_on <= now) || (p.active_from && p.active_from > now)) continue;
      const prev = out.get(p.code);
      if (!prev || String(p.active_from || '') > String(prev.active_from || '')) out.set(p.code, p);
    }
    return out;
  }

  // Belgian public holidays (paid parking usually doesn't apply: rule.special_day = 0).
  function easter(y) {
    const a = y % 19, b = Math.floor(y / 100), c = y % 100, d = Math.floor(b / 4), e = b % 4, f = Math.floor((b + 8) / 25),
      g = Math.floor((b - f + 1) / 3), h = (19 * a + b - d - g + 15) % 30, i = Math.floor(c / 4), k = c % 4,
      l = (32 + 2 * e + 2 * i - h - k) % 7, m = Math.floor((a + 11 * h + 22 * l) / 451), mo = Math.floor((h + l - 7 * m + 114) / 31);
    return Date.UTC(y, mo - 1, ((h + l - 7 * m + 114) % 31) + 1);
  }
  function isHoliday(y, m, d) {
    const fixed = ['1-1', '5-1', '7-21', '8-15', '11-1', '11-11', '12-25'];
    if (fixed.includes(`${m}-${d}`)) return true;
    const t = Date.UTC(y, m - 1, d), e = easter(y), day = 86400000;
    return [e + day, e + 39 * day, e + 50 * day].includes(t); // Easter Monday, Ascension, Whit Monday
  }
  // Date/time in Brussels, whatever the phone's time zone.
  function brussels(date = new Date()) {
    const p = Object.fromEntries(new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Brussels', year: 'numeric', month: 'numeric', day: 'numeric',
      hour: 'numeric', minute: 'numeric', weekday: 'short', hourCycle: 'h23' }).formatToParts(date).map((x) => [x.type, x.value]));
    return { y: +p.year, m: +p.month, d: +p.day, wd: p.weekday, min: +p.hour * 60 + +p.minute };
  }
  const DAY_KEYS = { Mon: 'mo', Tue: 'tu', Wed: 'we', Thu: 'th', Fri: 'fr', Sat: 'sa', Sun: 'so' };
  const hm = (s) => { const m = String(s || '').match(/^(\d{1,2}):(\d{2})/); return m ? +m[1] * 60 + +m[2] : null; };
  const dayPaid = (rule, t) => rule[DAY_KEYS[t.wd]] === 1 && !(isHoliday(t.y, t.m, t.d) && rule.special_day !== 1);
  // Is paying (or a disc) required right now under this rule? Unknown hours => assume yes.
  function paidNow(rule, date = new Date()) {
    if (!rule) return true; // unknown: never pretend it's free
    if (rule.type === 'FREE') return false;
    const t = brussels(date), start = hm(rule.start_hour), end = hm(rule.end_hour);
    if (!dayPaid(rule, t)) return false;
    if (start == null || end == null) return true;
    return t.min >= start && t.min < end;
  }
  // When does paying start again? (label like "tomorrow 09:00" / "Monday 09:00")
  function nextPaid(rule, date = new Date()) {
    const start = hm(rule?.start_hour);
    if (start == null) return null;
    const now = brussels(date);
    for (let i = 0; i < 9; i++) {
      const t = brussels(new Date(date.getTime() + i * 86400000));
      if (!dayPaid(rule, t) || (i === 0 && now.min >= start)) continue;
      const when = i === 0 ? 'today' : i === 1 ? 'tomorrow' : ({ Mon: 'Monday', Tue: 'Tuesday', Wed: 'Wednesday', Thu: 'Thursday', Fri: 'Friday', Sat: 'Saturday', Sun: 'Sunday' })[t.wd];
      return `${when} ${rule.start_hour.slice(0, 5)}`;
    }
    return null;
  }
  const euro = (v) => (v == null || v === '' ? null : '€' + Number(v).toFixed(2).replace('.', ','));
  const minutesText = (m) => { m = +m; return !m ? null : m % 60 ? (m > 60 ? `${Math.floor(m / 60)} h ${m % 60}` : `${m} min`) : `${m / 60} h`; };
  function scheduleText(rule) {
    const days = ['mo', 'tu', 'we', 'th', 'fr', 'sa', 'so'].filter((k) => rule[k] === 1);
    const names = { mo: 'Mon', tu: 'Tue', we: 'Wed', th: 'Thu', fr: 'Fri', sa: 'Sat', so: 'Sun' };
    const span = days.length === 6 && !days.includes('so') ? 'Mon–Sat' : days.length === 7 ? 'every day' : days.map((k) => names[k]).join(', ');
    return `${span}${rule.start_hour ? ' ' + rule.start_hour.slice(0, 5) + '–' + String(rule.end_hour).slice(0, 5) : ''}`;
  }
  // Short tariff description for display.
  function tariffText(rule) {
    if (!rule) return null;
    const parts = [];
    if (rule.type === 'DISK') parts.push('Blue disc');
    else if (euro(rule.charge60)) parts.push(`${euro(rule.charge60)}/h`);
    if (+rule.free_time) parts.push(`first ${rule.free_time} min free`);
    if (minutesText(rule.max_time)) parts.push(`max ${minutesText(rule.max_time)}`);
    parts.push(scheduleText(rule));
    if (euro(rule.fee)) parts.push(`fine ${euro(rule.fee)}`);
    return parts.join(' · ');
  }

  // ---------- decisions ----------
  function cardState(card) {
    if (!card) return { valid: false, why: 'No parking card set up yet — tap 🪪.' };
    const t = todayISO();
    if (card.from && t < card.from) return { valid: false, why: `Your card is only valid from ${fmtDate(card.from)}.` };
    if (card.to && t > card.to) return { valid: false, why: `Your card expired on ${fmtDate(card.to)}.` };
    return { valid: true };
  }
  const result = (status, title, detail, extra = {}) => ({ status, title, detail, ...extra });
  const cantVerify = (detail, extra) => result('unknown', "CAN'T VERIFY", detail, extra);
  function listProblem(list) {
    if (!list || list.status === 'ok') return null;
    if (list.status === 'changed') return `The City of Brussels changed its official sector document${list.date ? ' on ' + esc(list.date) : ''}. This app's street list may be outdated: update the app before relying on it.`;
    return `Could not confirm that the City's official sector list is unchanged (${esc(list.why || 'not checked yet')}).`;
  }

  // ---------- zone types and card scope (all municipalities) ----------
  // What kind of zone is this, for a residents' card? From the live rule of the zone when available.
  //   FREE · PAYABLE (card never valid) · DEROGATION (card valid in its sector) · DISK (blue zone; card valid in
  //   its sector) · RESIDENTS (residents-only spaces) · RESERVED (delivery, car-sharing…: card not valid) · null
  function zoneType(z, rule) {
    if (z.typreg === 0) return 'FREE';
    if (rule?.type) {
      if (rule.type === 'RESERVED') return z.typreg === 9 ? 'RESIDENTS' : 'RESERVED';
      return rule.type;
    }
    // Rule unknown: only colours that mean the same everywhere. Orange depends on the municipality.
    return { 1: 'PAYABLE', 3: 'DEROGATION', 4: 'DEROGATION', 5: 'DISK', 9: 'RESIDENTS' }[z.typreg] || null;
  }
  const isEvent = (z, rule) => z.typreg === 6 || /v[ée]nement/i.test(rule?.name || '');

  // Sector polygons ("mailles") published by parking.brussels, with their codes.
  async function liveMailles(box) {
    return (await wfs(PB_WFS, 'ParkingBrussels:permit_secteurs_mailles', box, 'parking.brussels sectors'))
      .map((f) => ({ p: f.properties || {}, g: f.geometry }));
  }
  // The list of mailles (no shapes) for the card setup.
  async function maillesList() {
    const url = `${PB_WFS}?service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json`
      + '&typeNames=ParkingBrussels:permit_secteurs_mailles&propertyName=zonecode,namefre,namedut';
    const json = await withTimeout(fetch(url, { cache: 'no-store' }).then((r) => r.json()), C.API_TIMEOUT_MS, 'parking.brussels sectors');
    return (json.features || []).map((f) => f.properties).filter((p) => p.zonecode);
  }

  // Does the card cover this place (for zones that accept residents' cards)?
  // Returns { state: 'yes' | 'no' | 'check' | 'streets', why } — 'streets' = decide with the City street lists.
  function cardScope(ctx, z, pt, mailles, accuracy = 0) {
    const muni = ctx.muni;
    const here = titleCase(z.mu_name_fr);
    const inside = (m) => closestOnSurface(pt.lat, pt.lon, m.g);
    for (const m of mailles) {
      if (ctx.lims.has(m.p.zonecode) && inside(m).m === 0) return { state: 'yes', why: `border zone ${esc(m.p.namefre || m.p.zonecode)} (on your card)` };
    }
    if (normalize(z.mu_name_fr) !== normalize(muni.mu)) {
      if (muni.kind === 'streets') return { state: 'streets' }; // City tolerance agreements are street lists
      return { state: 'no', why: `is in ${esc(here)}, not in ${esc(muni.name)}` };
    }
    switch (muni.kind) {
      case 'streets': return { state: 'streets' };
      case 'whole': return { state: 'yes', why: `your card is valid in this zone everywhere in ${esc(muni.name)}` };
      case 'wsl':
        return zoneType(z, null) === 'DISK' || z.typreg === 5
          ? { state: 'yes', why: `blue zone: your card is valid in all blue zones of ${esc(muni.name)}` }
          : { state: 'check', why: esc(muni.note) };
      case 'unknown': return { state: 'check', why: esc(muni.note) };
      case 'mailles': {
        if (!ctx.mailles.size) return { state: 'check', why: 'choose the areas (mailles) printed on your card in the card settings' };
        const mine = mailles.filter((m) => ctx.mailles.has(m.p.zonecode));
        const hit = mine.find((m) => inside(m).m === 0);
        if (hit) return { state: 'yes', why: `in your area ${esc(hit.p.namefre || hit.p.zonecode)}` };
        const near = mine.map((m) => ({ m, d: inside(m).m })).sort((a, b) => a.d - b.d)[0];
        if (near && near.d <= Math.max(C.BORDER_MARGIN_M, accuracy)) {
          return { state: 'check', why: `you are at the limit of your area ${esc(near.m.p.namefre || near.m.p.zonecode)}: check the street signs` };
        }
        return { state: 'no', why: 'is outside the areas on your card' };
      }
      default: return { state: 'check', why: 'unknown municipality rules' };
    }
  }
  // City street lists: a side with a condition in words ("côté parc"…).
  const sideCheck = (sets, odd) => sets.map((r) => (odd ? r.odd : r.even)).find((x) => x.check) || null;
  const bboxCenter = (f) => { const [x0, y0, x1, y1] = f.bb; const [lat, lon] = fromMerc((x0 + x1) / 2, (y0 + y1) / 2); return { lat, lon }; };

  // Status of one street surface for your card. Used by the map and the free-spot search.
  function surfaceStatus(ctx, f, addrs, rules, mailles) {
    const z = f.p, rule = rules?.get(z.ZoneId1) || null, type = zoneType(z, rule);
    const base = { f, zone: z, rule, color: ZONE_NAMES[z.typreg] ?? 'unknown' };
    if (type === 'FREE') return { ...base, kind: 'free' };
    if (type === 'PAYABLE') return { ...base, kind: z.typreg === 1 ? 'red' : 'pay' };
    if (type === 'RESERVED') return { ...base, kind: 'pay' };
    if (!type || isEvent(z, rule)) return { ...base, kind: 'unknown' };
    const notCovered = type === 'DISK' ? 'disc' : 'pay';
    const scope = cardScope(ctx, z, bboxCenter(f), mailles || []);
    if (scope.state === 'yes') return { ...base, kind: 'covered', near: [] };
    if (scope.state === 'no') return { ...base, kind: notCovered };
    if (scope.state === 'check') return { ...base, kind: 'check' };
    // City of Brussels street lists
    if (!ctx.sector) return { ...base, kind: 'check' };
    const idx = streetIndex(ctx.sector, z.pn_name_fr, z.pn_name_du);
    const st = idx >= 0 ? ctx.sector.streets[idx] : null;
    const sets = st ? ruleSetsFor(st, z.mu_name_fr, ctx.codes) : [];
    if (!sets.length) return { ...base, st, sets, kind: notCovered };
    if (ctx.listProblem) return { ...base, st, sets, kind: 'unknown' };
    if (sets.some((r) => r.odd.all && r.even.all)) return { ...base, st, sets, kind: 'covered', near: [] };
    if (sets.some((r) => r.odd.check || r.even.check)) return { ...base, st, sets, kind: 'check' };
    let near = addrs.filter((a) => a.surfaceId === f.id);
    if (!near.length) {
      near = addrs.filter((a) => a.i === st.i && a.commune === normalize(z.mu_name_fr)
        && closestOnSurface(a.a, a.o, f.g).m <= C.ADDR_TO_SURFACE_M);
    }
    near = near.map((a) => ({ ...a, cov: setsCover(sets, a.house) }));
    if (!near.length) return { ...base, st, sets, kind: 'check', near };
    const yes = near.filter((a) => a.cov).length;
    return { ...base, st, sets, near, kind: yes === near.length ? 'covered' : yes === 0 ? notCovered : 'mixed' };
  }

  // Full decision: the card/zone/number decision, then reserved spaces and roadworks at that spot.
  async function decide(ctx, lat, lon, accuracy) {
    const r = await decideZone(ctx, lat, lon, accuracy);
    if (!r.zone) return r; // no street found / can't verify / card invalid: nothing more to add
    const d = 0.0004, box = [lat - d, lon - d * 1.6, lat + d, lon + d * 1.6];
    const notes = [];
    // 1. Reserved spaces: these apply whatever your card.
    let spaces;
    try { spaces = await liveSpaces(box); } catch (e) { return cantVerify(`Could not check reserved parking spaces (${esc(e.message)}).`, { zone: r.zone }); }
    const reach = Math.max(4, accuracy || 0);
    const near = spaces.map((s) => ({ ...s, m: dist(lat, lon, s.lat, s.lon) })).filter((s) => s.m <= reach).sort((a, b) => a.m - b.m);
    const precise = accuracy == null || accuracy <= 6;
    const first = near[0];
    if (first && first.typres && !TIME_LIMITED.has(first.typres) && first.m <= 3 && precise) {
      return { ...r, status: 'pay', title: 'RESERVED', detail: `<b>${esc(first.label || 'Reserved space')}</b>: you can't park here, whatever your card. ${r.detail}`, reserved: first, notes };
    }
    const reservedNear = [...new Map(near.filter((s) => s.typres && !TIME_LIMITED.has(s.typres)).map((s) => [s.typres, s])).values()];
    for (const s of reservedNear) notes.push(`⚠️ ${esc(s.label)} ${Math.round(s.m)} m away — don't park on it.`);
    const timed = near.find((s) => TIME_LIMITED.has(s.typres) && s.m <= 3);
    let out = { ...r, notes };
    if (timed) {
      notes.unshift(`${esc(timed.label)}: parking time is limited here, your card doesn't extend it.`);
      if (out.status === 'free') out = { ...out, status: 'unknown', title: 'TIME LIMITED' };
    }
    // 2. Roadworks and traffic events (information: they may forbid parking, follow the signs).
    try {
      for (const w of worksAt(await liveWorks(box), lat, lon)) {
        notes.push(`🚧 ${esc(w.title)}${w.where ? ' — ' + esc(w.where) : ''}${w.to ? ' (until ' + esc(fmtDate(w.to)) + ')' : ''}. Parking may be forbidden: check the signs.`);
      }
    } catch (e) { notes.push(`Could not check roadworks (${esc(e.message)}).`); }
    // 3. Paid hours and prices of this zone (live rule, fetched by decideZone). Outside paid hours nobody pays.
    if (r.ruleError) notes.push(`Could not check the paid hours (${esc(r.ruleError)}): the app assumes they apply.`);
    const rule = r.rule;
    if (rule && r.zone.typreg !== 0) {
      out.tariff = tariffText(rule);
      const needsPaying = (out.status === 'pay' && out.title !== 'RESERVED') || ['PARKING DISC', 'CHECK THE NUMBER', 'CHECK YOUR CARD', 'CHECK THE SIGNS'].includes(out.title);
      if (needsPaying && !paidNow(rule)) {
        const next = nextPaid(rule);
        notes.unshift(`During paid hours: ${out.detail}`);
        out = { ...out, status: 'free', title: 'FREE NOW',
          detail: `No need to pay right now: here, ${rule.type === 'DISK' ? 'the parking disc' : 'paid parking'} applies <b>${esc(scheduleText(rule))}</b>${isHoliday(brussels().y, brussels().m, brussels().d) && rule.special_day !== 1 ? ' (not on public holidays)' : ''}.${next ? ` It starts again <b>${esc(next)}</b>.` : ''}` };
      }
    }
    return out;
  }

  async function decideZone(ctx, lat, lon, accuracy) {
    const cs = cardState(ctx.card);
    if (!ctx.card) return cantVerify('Add your residents\' parking card first (card button, top right).');
    if (!cs.valid) return result('pay', 'PAY', cs.why);
    if (!navigator.onLine) return cantVerify('No internet connection. The app only answers with live data.');
    if (accuracy != null && accuracy > C.MAX_GPS_ACCURACY_M) {
      return cantVerify(`GPS is not precise enough yet (±${Math.round(accuracy)} m, need ±${C.MAX_GPS_ACCURACY_M} m). Waiting for a better fix…`);
    }
    const d = 0.0007;
    const box = [lat - d, lon - d * 1.6, lat + d, lon + d * 1.6];
    let zones;
    try { zones = await liveZones(pad(box)); } catch (e) { return cantVerify(`Could not reach parking.brussels (${esc(e.message)}).`); }
    let f = null, fm = Infinity;
    for (const z of zones) { const c = closestOnSurface(lat, lon, z.g); if (c.m < fm) { fm = c.m; f = z; } }
    if (!f || fm > C.ZONE_SNAP_M + Math.min(accuracy || 0, 15)) return cantVerify('This point is not on a street with parking rules. Put the pin on the street, where you park.');

    const z = f.p;
    const code = z.ZoneId1 || z.zoneid1;
    let rule = null, ruleError = null;
    if (code && z.typreg !== 0) { try { rule = (await liveRules(code)).get(code) || null; } catch (e) { ruleError = e.message; } }
    const extra = { zone: z, surface: f, rule, ruleError };
    const where = `<b>${esc(z.pn_name_fr)}</b>`;
    const type = zoneType(z, rule);
    if (type === 'FREE') return result('free', 'FREE ✓', `${where} has no parking rules: free for everyone.`, extra);
    if (isEvent(z, rule)) return cantVerify(`${where} is an event zone: the normal rules don't apply. Follow the signs.`, extra);
    if (type === 'PAYABLE') {
      return result('pay', 'PAY', `${where} is ${z.typreg === 1 ? 'a red zone' : `a ${esc(ZONE_NAMES[z.typreg] || '')} zone`}: residents' cards are not valid here. Pay at the meter or with an app.`, extra);
    }
    if (type === 'RESERVED') return result('pay', 'PAY', `${where}: reserved zone (${esc(rule?.name || 'reserved use')}). Residents' cards are not valid here.`, extra);
    if (!type) return cantVerify(`${where}: the rules of this ${esc(ZONE_NAMES[z.typreg] || '')} zone could not be checked. Orange zones accept residents' cards only in some municipalities: follow the signs.`, extra);

    const notCovered = type === 'DISK'
      ? (txt) => result('unknown', 'PARKING DISC', `${txt} Blue zone: put your parking disc, time is limited.`, extra)
      : (txt) => result('pay', 'PAY', `${txt} Pay at the meter or with an app.`, extra);

    // Which municipality / sector / mailles does the card cover?
    let mailles = [];
    if (ctx.muni.kind === 'mailles' || ctx.lims.size) {
      try { mailles = await liveMailles(pad(box)); } catch (e) { return cantVerify(`Could not get the sector map from parking.brussels (${esc(e.message)}).`, extra); }
    }
    const scope = cardScope(ctx, z, { lat, lon }, mailles, accuracy || 0);
    if (scope.state === 'yes') return result('free', 'FREE ✓', `${where}: ${scope.why}.`, extra);
    if (scope.state === 'no') return notCovered(`${where} ${scope.why}: your card doesn't cover it.`);
    if (scope.state === 'check') return result('unknown', 'CHECK YOUR CARD', `${where}: ${scope.why}.`, extra);

    // City of Brussels: street + house-number lists.
    const idx = streetIndex(ctx.sector, z.pn_name_fr, z.pn_name_du);
    const st = idx >= 0 ? ctx.sector.streets[idx] : null;
    const sets = st ? ruleSetsFor(st, z.mu_name_fr, ctx.codes) : [];
    if (!sets.length) {
      const hint = st && st.tol.some((t) => normalize(t.commune) === normalize(z.mu_name_fr)) ? ' (only with a tolerance code your card does not have)' : '';
      return notCovered(`${where} (${esc(titleCase(z.mu_name_fr))}) isn't in your sector ${esc(ctx.sector.name)}${hint}.`);
    }
    // From here the answer depends on the City's list: it must be confirmed unchanged.
    if (ctx.listProblem) return cantVerify(ctx.listProblem, extra);
    const ruleText = sets.map((r) => `${esc(r.label)}: odd <b>${esc(r.odd.text)}</b>, even <b>${esc(r.even.text)}</b>`).join('; ');
    if (sets.some((r) => r.odd.all && r.even.all)) return result('free', 'FREE ✓', `${where}: your card covers the whole street.`, extra);
    const bothCheck = sideCheck(sets, true) && sideCheck(sets, false);
    if (bothCheck) return result('unknown', 'CHECK THE SIGNS', `${where} is in your sector only under a condition: “${esc(bothCheck.text)}”.`, extra);

    let addrs;
    try { addrs = await liveAddresses(ctx.sector, box, zones); } catch (e) { return cantVerify(`Could not get the official house numbers (${esc(e.message)}). ${ruleText}.`, extra); }
    // House numbers of this street on this municipality's side.
    const mine = addrs.filter((a) => a.i === st.i && a.commune === normalize(z.mu_name_fr));
    let best = null, bd = Infinity;
    for (const a of mine) { const m = dist(lat, lon, a.a, a.o); if (m < bd) { bd = m; best = a; } }
    if (!best) return cantVerify(`Your card covers only part of ${where} (${ruleText}). Look at the house number in front of you.`, extra);
    const cond = sideCheck(sets, best.house.n % 2 === 1);
    if (cond && !setsCover(sets, best.house)) {
      return result('unknown', 'CHECK THE SIGNS', `In front of <b>${esc(st.fr)} ${esc(best.h)}</b>: this side is in your sector only under a condition: “${esc(cond.text)}”.`, extra);
    }

    const margin = Math.max(C.BORDER_MARGIN_M, accuracy || 0);
    const close = mine.filter((a) => dist(lat, lon, a.a, a.o) <= bd + margin);
    const yes = close.filter((a) => setsCover(sets, a.house)), no = close.filter((a) => !setsCover(sets, a.house));
    if (yes.length && no.length) {
      const list = (xs) => [...new Set(xs.map((a) => a.h))].slice(0, 4).join(', ');
      return result('unknown', 'CHECK THE NUMBER', `You're at a limit on ${where}: free in front of <b>${esc(list(yes))}</b>, pay in front of <b>${esc(list(no))}</b>.`, extra);
    }
    const cov = setsCover(sets, best.house);
    const front = `In front of <b>${esc(st.fr)} ${esc(best.h)}</b>`;
    return cov
      ? result('free', 'FREE ✓', `${front}. Your card covers this side of the street${cov.label !== 'your sector' ? ' (' + esc(cov.label) + ')' : ''}.`, extra)
      : notCovered(`${front}. Your card doesn't cover this number (${ruleText}).`);
  }

  // Street surfaces + their status for an area (map layer, free-spot search). All live.
  async function liveArea(ctx, box, { withAddresses = true } = {}) {
    const needMailles = ctx.muni.kind === 'mailles' || ctx.lims.size;
    const [zones, rules, mailles] = await Promise.all([
      liveZones(pad(box)),
      liveRules().catch(() => null),
      needMailles ? liveMailles(pad(box)) : Promise.resolve([]),
    ]);
    const addrs = withAddresses && ctx.muni.kind === 'streets' && ctx.sector ? await liveAddresses(ctx.sector, box, zones) : [];
    const inBox = (f) => f.g && polysOf(f.g).some((r) => r[0].some(([x, y]) => {
      const [la, lo] = fromMerc(x, y); return la >= box[0] && la <= box[2] && lo >= box[1] && lo <= box[3];
    }));
    return zones.filter(inBox).map((f) => surfaceStatus(ctx, f, addrs, rules, mailles));
  }

  // Nearest covered spots, from live data only. Returns up to `count`, one per street.
  async function liveFreeSpots(ctx, lat, lon, count = 3) {
    if (!cardState(ctx.card).valid || ctx.listProblem) return [];
    const d = 0.0035;
    const surfaces = await liveArea(ctx, [lat - d, lon - d * 1.6, lat + d, lon + d * 1.6]);
    const spots = [];
    for (const s of surfaces) {
      if (s.kind === 'covered') {
        const c = closestOnSurface(lat, lon, s.f.g);
        spots.push({ lat: c.lat, lon: c.lon, m: dist(lat, lon, c.lat, c.lon), street: s.zone.pn_name_fr, commune: s.zone.mu_name_fr, label: s.st?.fr || s.zone.pn_name_fr });
      } else if (s.kind === 'mixed') {
        for (const a of s.near.filter((x) => x.cov)) {
          const c = closestOnSurface(a.a, a.o, s.f.g); // the curb in front of that house
          spots.push({ lat: c.lat, lon: c.lon, m: dist(lat, lon, c.lat, c.lon), street: s.zone.pn_name_fr, commune: s.zone.mu_name_fr, label: `${s.st.fr} ${a.h}` });
        }
      }
    }
    spots.sort((x, y) => x.m - y.m);
    const out = [], seen = new Set();
    for (const s of spots) {
      if (s.m < 3) continue; // that's where you already are
      const k = normalize(s.street);
      if (seen.has(k)) continue;
      seen.add(k); out.push(s);
      if (out.length >= count) break;
    }
    return out;
  }

  window.PT = {
    C, ZONE_NAMES, IN_APP, PB_WFS, esc, fmtDate, todayISO, normalize, dist, withTimeout,
    parseHouse, parseRule, ruleCovers, buildSector, streetIndex, ruleSetsFor, setsCover,
    checkOfficialList, liveZones, liveAddresses, liveArea, liveFreeSpots, surfaceStatus, surfaceLatLngs,
    liveSpaces, liveWorks, liveCarParks, liveChargers, RESERVED, TIME_LIMITED,
    liveRules, paidNow, nextPaid, tariffText, scheduleText, isHoliday,
    zoneType, cardScope, liveMailles, maillesList,
    cardState, decide, listProblem, cantVerify,
  };
})();
