#!/usr/bin/env node
/* Parity recorder for the .NET port (docs/architecture.md section 6.3).
 *
 * Runs the real decision code of the repository root (municipalities.js, city-sectors.js, sectors.js, live.js) in
 * Node with a minimal `window`, `navigator` and `fetch`, against the live services, with a fixed clock per scenario.
 * For every scenario it writes, into test/fixtures/parity/<scenario>/:
 *   - scenario.json: the inputs (card, point, accuracy, clock) and the answer live.js gave (status, title, plain-text
 *     detail, notes, tariff), plus the result of the official-list check and `requests`, the ordered list of every
 *     request live.js made (repeats included), which the C# tests compare with the requests they make;
 *   - one <hash>.json per HTTP request the scenario made, keyed by method and URL, with the response.
 * The C# parity tests replay these files through the parsers and the decider and must give the same answer.
 *
 * Usage:   node tools/parity/record.mjs [--dry] [scenarioName ...]
 *   --dry   print the answers without writing any file (to choose places).
 *   --help  print this usage.
 * Exit code: 0 when every scenario was recorded; 1 for an unknown argument or scenario name, or when a scenario fails.
 * Needs Node 22 or later and network access. The fixtures are committed; run this once, and again only to refresh them.
 * The recorder and live.js are deleted at cut-over; the fixtures stay as regression data.
 */
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

process.env.TZ = 'Europe/Brussels'; // live.js reads the card dates in local time

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outRoot = join(root, 'test', 'fixtures', 'parity');
const RealDate = Date;
const realFetch = globalThis.fetch;
const USER_AGENT = 'ParkingTrackerParityRecorder';
const BXL_SITE = 'https://www.bruxelles.be';

// ---------- the browser pieces live.js needs ----------
globalThis.window = globalThis;
window.Capacitor = {}; // "in the app": absolute URLs, no dev-server proxy
Object.defineProperty(globalThis, 'navigator', { value: { onLine: true }, configurable: true, writable: true });
for (const file of ['municipalities.js', 'city-sectors.js', 'sectors.js', 'live.js']) {
  vm.runInThisContext(readFileSync(join(root, file), 'utf8'), { filename: file });
}
const PT = window.PT;
const MUNI = Object.fromEntries(window.MUNICIPALITIES.map((m) => [m.id, m]));

function installClock(iso) {
  const fixed = RealDate.parse(iso);
  globalThis.Date = class FixedDate extends RealDate {
    constructor(...args) { if (args.length === 0) super(fixed); else super(...args); }
    static now() { return fixed; }
  };
}

// ---------- scenarios ----------
const TUESDAY = '2026-11-17T10:00:00Z'; // 11:00 in Brussels, paid hours
const SUNDAY = '2026-11-15T10:00:00Z';
const HOLIDAY = '2026-11-11T10:00:00Z'; // Armistice Day, a Wednesday

const card = (municipality, extra = {}) => ({
  number: 'B000-000-000', plate: '1ABC234', municipality, sector: null, from: '2026-01-01', to: '2027-12-31',
  tolerances: [], mailles: [], lims: [], ...extra,
});
const LOUISE_CARD = card('bruxelles', { sector: 'louise' });
const SCHUMAN_CARD = card('bruxelles', { sector: 'schuman' });

const LOUISE_216 = { lat: 50.826647, lon: 4.364997 }; // on the surface in front of Avenue Louise 216
const CHATELAIN = { lat: 50.824905, lon: 4.360564 }; // Place du Chatelain, Ixelles
const BAILLI_TUNNEL = { lat: 50.826030, lon: 4.365081 };
const RED_ZONE = { lat: 50.826043, lon: 4.360519 }; // Rue du Bailli, Ixelles, red

const scenarios = [
  { name: 'louise-216-city-louise', kind: 'decide', clock: TUESDAY, point: LOUISE_216, accuracy: 5, card: LOUISE_CARD },
  { name: 'chatelain-city-card-pays', kind: 'decide', clock: TUESDAY, point: CHATELAIN, accuracy: 5, card: LOUISE_CARD },
  { name: 'chatelain-ixelles-ouest-in', kind: 'decide', clock: TUESDAY, point: CHATELAIN, accuracy: 5, card: card('ixelles', { mailles: ['IXLR_Ouest'] }) },
  { name: 'chatelain-ixelles-nord-out', kind: 'decide', clock: TUESDAY, point: CHATELAIN, accuracy: 5, card: card('ixelles', { mailles: ['IXLR_Nord'] }) },
  { name: 'etterbeek-green', kind: 'decide', clock: TUESDAY, point: { lat: 50.836724, lon: 4.381871 }, accuracy: 5, card: card('etterbeek') },
  { name: 'uccle-check-your-card', kind: 'decide', clock: TUESDAY, point: { lat: 50.800428, lon: 4.337810 }, accuracy: 5, card: card('uccle') },
  { name: 'saint-josse-unknown', kind: 'decide', clock: TUESDAY, point: { lat: 50.855654, lon: 4.369047 }, accuracy: 5, card: card('saint-josse') },
  { name: 'tunnel-bailli-louise', kind: 'decide', clock: TUESDAY, point: BAILLI_TUNNEL, accuracy: 5, card: LOUISE_CARD },
  { name: 'red-zone', kind: 'decide', clock: TUESDAY, point: RED_ZONE, accuracy: 5, card: LOUISE_CARD },
  { name: 'reserved-delivery-space', kind: 'decide', clock: TUESDAY, point: { lat: 50.828526, lon: 4.362997 }, accuracy: 2, card: LOUISE_CARD },
  { name: 'check-street-nerviens-schuman', kind: 'decide', clock: TUESDAY, point: { lat: 50.839128, lon: 4.387751 }, accuracy: 5, card: SCHUMAN_CARD },
  { name: 'check-the-number-washington', kind: 'decide', clock: TUESDAY, point: { lat: 50.825530, lon: 4.363590 }, accuracy: 5, card: LOUISE_CARD },
  { name: 'sunday-outside-paid-hours', kind: 'decide', clock: SUNDAY, point: CHATELAIN, accuracy: 5, card: LOUISE_CARD },
  { name: 'public-holiday', kind: 'decide', clock: HOLIDAY, point: CHATELAIN, accuracy: 5, card: LOUISE_CARD },
  {
    name: 'city-list-changed', kind: 'decide', clock: TUESDAY, point: LOUISE_216, accuracy: 5, card: LOUISE_CARD,
    // The City's answer is simulated: a different date and a different document.
    simulate: (method, url) => {
      if (!url.startsWith(BXL_SITE)) return null;
      return method === 'HEAD'
        ? { status: 200, headers: { etag: '"simulated-changed"', 'last-modified': 'Mon, 02 Nov 2026 09:00:00 GMT' } }
        : { status: 200, bytes: Buffer.from('%PDF-1.4 simulated changed document') };
    },
  },
  {
    name: 'source-failed', kind: 'decide', clock: TUESDAY, point: LOUISE_216, accuracy: 5, card: LOUISE_CARD,
    // parking.brussels is simulated as down.
    simulate: (method, url) => (url.includes('regulation_zones') ? { status: 500, bodyText: 'Internal Server Error' } : null),
  },
  { name: 'poor-gps', kind: 'decide', clock: TUESDAY, point: LOUISE_216, accuracy: 60, card: LOUISE_CARD },
  { name: 'free-spots-louise', kind: 'freeSpots', clock: TUESDAY, point: LOUISE_216, accuracy: 5, card: LOUISE_CARD },
  { name: 'area-louise-city', kind: 'area', clock: TUESDAY, point: LOUISE_216, accuracy: 5, card: LOUISE_CARD },
  { name: 'area-ixelles-ouest', kind: 'area', clock: TUESDAY, point: CHATELAIN, accuracy: 5, card: card('ixelles', { mailles: ['IXLR_Ouest'] }) },
];

// ---------- recording ----------
const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');
const fileKey = (method, url) => sha256(`${method} ${url}`).slice(0, 16);
const ENTITIES = { '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'" };
const plain = (html) => (html == null ? null : String(html).replace(/<[^>]+>/g, '').replace(/&(?:amp|lt|gt|quot|#39);/g, (m) => ENTITIES[m]));

function installFetch(log, requests, simulate) {
  globalThis.fetch = async (input, options = {}) => {
    const url = String(input);
    const method = String(options.method || 'GET').toUpperCase();
    const isDocument = url.startsWith(BXL_SITE) && method === 'GET';
    let entry, payload;
    requests.push({ method, url });
    const sim = simulate?.(method, url);
    if (sim) {
      payload = sim.bytes ?? Buffer.from(sim.bodyText ?? '');
      entry = { simulated: true, status: sim.status, headers: sim.headers };
    } else {
      const response = await realFetch(url, { method, headers: { 'User-Agent': USER_AGENT } });
      payload = method === 'HEAD' ? Buffer.alloc(0) : Buffer.from(await response.arrayBuffer());
      const headers = {};
      for (const name of ['etag', 'last-modified']) if (response.headers.get(name)) headers[name] = response.headers.get(name);
      entry = { status: response.status, headers: Object.keys(headers).length ? headers : undefined };
    }
    entry = { method, url, ...entry };
    if (isDocument) {
      entry.bodySha256 = sha256(payload);
      entry.isPdf = payload.length > 4 && payload.subarray(0, 4).toString('latin1') === '%PDF';
    } else if (method !== 'HEAD') {
      const text = payload.toString('utf8');
      try { entry.body = JSON.parse(text); } catch { entry.bodyText = text; }
    }
    log.set(fileKey(method, url), entry);
    return new Response(method === 'HEAD' ? null : payload, { status: entry.status, headers: entry.headers });
  };
}

function buildSector(cardValue, muni) {
  if (muni.kind !== 'streets') return null;
  const def = window.CITY_SECTORS[cardValue?.sector || 'louise'] || window.CITY_SECTORS.louise;
  const extra = window.SECTORS[def.id];
  return PT.buildSector({
    ...def, tolerances: extra?.tolerances || [],
    sources: [...def.sources, ...((extra?.sources || []).filter((s) => !def.sources.some((d) => d.url === s.url)))],
  });
}

async function run(scenario) {
  installClock(scenario.clock);
  const log = new Map();
  const requests = [];
  installFetch(log, requests, scenario.simulate);
  const muni = MUNI[scenario.card.municipality] || MUNI.bruxelles;
  const sector = buildSector(scenario.card, muni);
  const list = sector ? await PT.checkOfficialList(sector) : null; // as app.js does before every decision
  const ctx = {
    card: scenario.card, muni, sector,
    codes: new Set(scenario.card.tolerances), mailles: new Set(scenario.card.mailles), lims: new Set(scenario.card.lims),
    listProblem: sector ? PT.listProblem(list) : null,
  };
  const { lat, lon } = scenario.point;
  let expected;
  if (scenario.kind === 'decide') {
    const r = await PT.decide(ctx, lat, lon, scenario.accuracy);
    expected = {
      status: r.status, title: r.title, detail: plain(r.detail), notes: (r.notes || []).map(plain), tariff: r.tariff ?? null,
      zoneId: r.zone?.id ?? null, zoneStreet: r.zone?.pn_name_fr ?? null,
    };
  } else if (scenario.kind === 'freeSpots') {
    expected = (await PT.liveFreeSpots(ctx, lat, lon, 3)).map((s) => ({ lat: s.lat, lon: s.lon, metres: s.m, street: s.street, commune: s.commune, label: s.label }));
  } else {
    const d = 0.0012;
    const surfaces = await PT.liveArea(ctx, [lat - d, lon - d * 1.6, lat + d, lon + d * 1.6]);
    expected = surfaces.map((s) => ({ id: s.f.id, kind: s.kind, color: s.color, street: s.zone?.pn_name_fr ?? null, label: s.st?.fr ?? null, near: (s.near || []).length, covered: (s.near || []).filter((a) => a.cov).length }));
  }
  globalThis.Date = RealDate;
  globalThis.fetch = realFetch;
  const definition = {
    name: scenario.name, kind: scenario.kind, clock: scenario.clock, point: scenario.point, accuracy: scenario.accuracy, card: scenario.card,
    officialList: list ? { status: list.status, document: list.doc ?? null, date: list.date ?? null, why: list.why ?? null } : null,
    requests,
    expected,
  };
  return { definition, log };
}

const USAGE = 'Usage: node tools/parity/record.mjs [--dry] [--help] [scenarioName ...]';
const args = process.argv.slice(2);
const unknownFlags = args.filter((a) => a.startsWith('-') && !['--dry', '--help'].includes(a));
if (args.includes('--help')) {
  console.log(USAGE);
  process.exit(0);
}
if (unknownFlags.length) {
  console.error(`Unknown argument: ${unknownFlags.join(', ')}
${USAGE}`);
  process.exit(1);
}
const dry = args.includes('--dry');
const wanted = args.filter((a) => !a.startsWith('-'));
const unknownNames = wanted.filter((name) => !scenarios.some((s) => s.name === name));
if (unknownNames.length) {
  console.error(`Unknown scenario: ${unknownNames.join(', ')}
${USAGE}`);
  process.exit(1);
}

const failed = [];
for (const scenario of scenarios.filter((s) => !wanted.length || wanted.includes(s.name))) {
  let result;
  try {
    result = await run(scenario);
  } catch (error) {
    globalThis.Date = RealDate;
    globalThis.fetch = realFetch;
    console.error(`${scenario.name}: FAILED (${error?.message ?? error})`);
    failed.push(scenario.name);
    continue;
  }
  const { definition, log } = result;
  const e = definition.expected;
  console.log(`${scenario.name}: ${Array.isArray(e) ? `${e.length} items` : `${e.status} | ${e.title} | ${e.detail}`} (${definition.requests.length} requests)`);
  if (Array.isArray(e) && scenario.kind === 'area') console.log('  kinds: ' + JSON.stringify(e.reduce((c, x) => ({ ...c, [x.kind]: (c[x.kind] || 0) + 1 }), {})));
  else if (Array.isArray(e)) console.log('  ' + JSON.stringify(e.slice(0, 3)));
  else if (e.notes.length) console.log('  notes: ' + JSON.stringify(e.notes));
  if (dry) continue;
  const dir = join(outRoot, scenario.name);
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, 'scenario.json'), JSON.stringify(definition, null, 2) + '\n');
  for (const [key, entry] of log) writeFileSync(join(dir, `${key}.json`), JSON.stringify(entry) + '\n');
}
if (failed.length) {
  console.error(`${failed.length} scenario(s) failed: ${failed.join(', ')}`);
  process.exit(1);
}
