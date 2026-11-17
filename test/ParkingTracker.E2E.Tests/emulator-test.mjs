// Runs the .NET MAUI Android app in an emulator (GitHub Actions) and checks what it really does on Android:
// permissions, GPS, the official-list check and the answers at known places, through the Blazor web view.
//
// Usage (emulator already booted, adb on PATH):  node test/ParkingTracker.E2E.Tests/emulator-test.mjs path/to/ParkingTracker-test.apk
// Output: emulator-report/ (report.json, summary.md, screenshot-*.png, logcat.txt, crash.txt) and exit code 1 on failure.
//
// The test APK is the Release build made with -p:EnableTestHooks=true: its web view can be inspected through the Chrome
// DevTools Protocol, and window.ptTest (setCard, getCard, decide, freeSpots, officialList, platformStatus, screenState, startTimer)
// calls the app's own C# code. It also checks the parking alerts of ADR-0002: the foreground service, a parking alert while the app is
// in the background and a timer reminder. See docs/architecture.md section 6.3.
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync, appendFileSync } from 'node:fs';
import { join } from 'node:path';

const APK = process.argv[2];
const PKG = 'be.parkingtracker.app';
const ACTIVITY = `${PKG}/${PKG}.MainActivity`; // the Java name is fixed by [Activity(Name = ...)], see ADR-0001
const OUT = 'emulator-report';
// A short drive that ends at Avenue Louise 216: fixes 2 s apart and about 24 m apart (12 m/s, above the 5.5 m/s of the detector).
const DRIVE_STEPS = 14;
const DRIVE_STEP_DEGREES = 0.00022;
const DRIVE_KNOTS = 23; // about 11.8 m/s
const HOME = { name: 'Avenue Louise 216', lat: 50.82655, lon: 4.36505, expect: ['FREE ✓'] };
const CHATELAIN = { name: 'Place du Châtelain (Ixelles)', lat: 50.82480, lon: 4.36040, expect: ['PAY', 'FREE NOW'] };
const TEST_CARD = { number: 'TEST', plate: '1TST000', sector: 'louise', from: '2026-01-01', to: '2099-12-31', tolerances: [] };
// What logcat shows when .NET or Blazor lets an exception escape.
const DOTNET_FAILURE = /FATAL UNHANDLED EXCEPTION|Unhandled managed exception|Unhandled exception rendering component|AndroidRuntime.*FATAL EXCEPTION/i;

if (!APK) {
  console.error('Usage: node emulator-test.mjs path/to/ParkingTracker-test.apk');
  process.exit(2);
}

mkdirSync(OUT, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
// Arguments go to adb as an array, never through a shell, so that nothing taken from argv is interpreted as shell syntax.
const adb = (args, opts = {}) => execFileSync('adb', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...opts }).trim();
const tryAdb = (args, opts = {}) => { try { return adb(args, opts); } catch (e) { return `ERR ${e.message.split('\n')[0]}`; } };
const geo = (p) => tryAdb(['emu', 'geo', 'fix', String(p.lon), String(p.lat)]); // longitude first
// A fix with a speed: emu geo fix <lon> <lat> <altitude> <satellites> <velocity in knots>. Without a speed the emulator may report zero.
const geoMoving = (p, knots) => tryAdb(['emu', 'geo', 'fix', String(p.lon), String(p.lat), '0', '10', String(knots)]);
const pressHome = () => tryAdb(['shell', 'input', 'keyevent', 'KEYCODE_HOME']);
const dumpsys = (args) => tryAdb(['shell', 'dumpsys', ...args], { maxBuffer: 64 * 1024 * 1024 });
// Polls the notifications of the phone until the pattern shows; returns the matching line or null.
const waitForNotification = async (pattern, ms, each = null) => {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    const line = dumpsys(['notification', '--noredact']).split(/\r?\n/).find((l) => pattern.test(l));
    if (line) return line.trim();
    if (each) each();
    await sleep(5000);
  }
  return null;
};
const screenshot = (name) => {
  const r = spawnSync('adb', ['exec-out', 'screencap', '-p'], { maxBuffer: 50 * 1024 * 1024 });
  if (r.status === 0) writeFileSync(join(OUT, `screenshot-${name}.png`), r.stdout);
};

const checks = [];
let step = 'start';
const stepTo = (s) => { step = s; console.log(`--- ${s}`); };
const check = (name, ok, detail) => { checks.push({ name, ok: !!ok, detail }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`); };

// ---------- Chrome DevTools Protocol client (Node 22 has a global WebSocket) ----------
async function connectWebView() {
  for (let i = 0; i < 60; i++) {
    const pid = tryAdb(['shell', 'pidof', PKG]);
    const sockets = tryAdb(['shell', 'cat', '/proc/net/unix']);
    const m = sockets.match(new RegExp(`@?(webview_devtools_remote_${pid.split(/\s+/)[0]})`));
    if (m) {
      tryAdb(['forward', '--remove-all']);
      adb(['forward', 'tcp:9222', `localabstract:${m[1]}`]);
      try {
        const pages = await (await fetch('http://127.0.0.1:9222/json')).json();
        const page = pages.find((p) => p.type === 'page' && p.webSocketDebuggerUrl);
        if (page) return page.webSocketDebuggerUrl;
      } catch { /* not ready */ }
    }
    await sleep(2000);
  }
  throw new Error('WebView devtools socket not found (is this the build made with -p:EnableTestHooks=true?)');
}
const logs = [];
let ws, nextId = 0;
const pending = new Map();
function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++nextId;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
    setTimeout(() => { if (pending.has(id)) { pending.delete(id); reject(new Error(`CDP ${method} timed out during step: ${step}`)); } }, 45000);
  });
}
async function evaluate(expression) {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result?.value;
}
async function cdp() {
  ws = new WebSocket(await connectWebView());
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  ws.onmessage = (m) => {
    const d = JSON.parse(m.data);
    if (d.id && pending.has(d.id)) { const p = pending.get(d.id); pending.delete(d.id); d.error ? p.reject(new Error(d.error.message)) : p.resolve(d.result); return; }
    if (d.method === 'Runtime.consoleAPICalled') logs.push(`${d.params.type}: ${d.params.args.map((a) => a.value ?? a.description ?? '').join(' ')}`);
    if (d.method === 'Runtime.exceptionThrown') logs.push(`EXCEPTION: ${d.params.exceptionDetails.exception?.description || d.params.exceptionDetails.text}`);
  };
  await send('Runtime.enable');
  await send('Page.enable').catch(() => {});
}
// Evaluates expr until it is truthy. `each` (optional) runs every `everyMs` while waiting, for example to send the emulator a GPS fix again.
const waitFor = async (expr, ms = 60000, each = null, everyMs = 2000) => {
  const end = Date.now() + ms;
  let lastEach = 0;
  while (Date.now() < end) {
    if (each && Date.now() - lastEach >= everyMs) { each(); lastEach = Date.now(); }
    try { const v = await evaluate(expr); if (v) return v; } catch { /* page reloading */ }
    await sleep(1000);
  }
  return null;
};
// What the main screen holds (tracking, last fix and its age, headline, busy), for the detail of a failed check.
const screenState = async () => { try { return JSON.stringify(await evaluate('ptTest.screenState()')); } catch (e) { return `screenState failed: ${e.message}`; } };
const blazorErrorUiHidden = `(() => { const e = document.querySelector('#blazor-error-ui'); return !!e && getComputedStyle(e).display === 'none'; })()`;

// ---------- the test ----------
async function main() {
  const report = { device: {}, checks, scenarios: {}, logs };
  report.device.android = tryAdb(['shell', 'getprop', 'ro.build.version.release']);
  report.device.webview = tryAdb(['shell', 'dumpsys', 'package', 'com.google.android.webview']).match(/versionName=(\S+)/)?.[1] || 'unknown';

  stepTo('install');
  // Install and grant what a user grants on first launch ("Allow all the time").
  console.log(adb(['install', '-r', '-g', APK]));
  for (const p of ['ACCESS_FINE_LOCATION', 'ACCESS_COARSE_LOCATION', 'ACCESS_BACKGROUND_LOCATION']) tryAdb(['shell', 'pm', 'grant', PKG, `android.permission.${p}`]);
  // Notifications (Android 13+) and exact alarms, which a user allows on the first timer.
  tryAdb(['shell', 'pm', 'grant', PKG, 'android.permission.POST_NOTIFICATIONS']);
  tryAdb(['shell', 'appops', 'set', PKG, 'SCHEDULE_EXACT_ALARM', 'allow']);
  tryAdb(['shell', 'settings', 'put', 'secure', 'location_mode', '3']);
  geo(HOME);
  tryAdb(['logcat', '-c']);

  stepTo('launch');
  tryAdb(['shell', 'am', 'start', '-n', ACTIVITY]);
  await sleep(8000);
  report.aliveAfterLaunch = tryAdb(['shell', 'pidof', PKG]);
  stepTo('connect to WebView');
  await cdp();
  stepTo('wait for the app');
  await waitFor('!!window.ptTest && !!document.querySelector(\'.hero-title\')', 120000);
  check('App runs in the .NET MAUI web view with the test hooks', await evaluate('!!window.ptTest && typeof window.ptTest.decide === \'function\''));

  stepTo('add test card');
  await evaluate(`ptTest.setCard(${JSON.stringify(JSON.stringify(TEST_CARD))}).then(() => true)`);
  const saved = JSON.parse((await evaluate('ptTest.getCard()')) || 'null');
  check('Test card saved and read back', saved?.sector === 'louise' && saved?.municipality === 'bruxelles' && saved?.plate === TEST_CARD.plate, JSON.stringify(saved));
  console.log(`screen state after setCard: ${await screenState()}`);

  stepTo('platform status');
  const platform = await evaluate('ptTest.platformStatus()');
  report.platform = platform;
  check('Location allowed all the time', platform?.locationForeground === 'granted' && platform?.locationBackground === 'granted', JSON.stringify(platform));
  check('Device is online', platform?.online === true, JSON.stringify(platform));
  check('Notifications and exact alarms allowed', platform?.notifications === true && platform?.exactAlarms === true, JSON.stringify(platform));

  stepTo('official list');
  // The official City list check through the app's HTTP clients.
  const list = await evaluate('ptTest.officialList()');
  report.list = list;
  check('City sector list confirmed unchanged', list?.state === 'ok', JSON.stringify(list));

  stepTo('answers');
  // Live answers at known places, through the app's own decision code.
  for (const s of [HOME, CHATELAIN]) {
    const r = await evaluate(`ptTest.decide(${s.lat}, ${s.lon}, 5)`);
    report.scenarios[s.name] = r;
    check(`Answer at ${s.name}`, s.expect.includes(r?.title), `${r?.title} — ${r?.detail}`);
  }

  stepTo('GPS on screen');
  // The emulator only delivers a fix when it is sent, so fixes are also re-sent during the wait for the position.
  // Real GPS path: the emulator's GPS is set to Avenue Louise 216; the screen must show the answer.
  for (let i = 0; i < 12; i++) { geo(HOME); await sleep(1500); }
  const gps = await waitFor('/My position/.test(document.body.innerText) && document.body.innerText.match(/My position[^\\n]*/)[0]', 60000, () => geo(HOME), 2000);
  report.gps = gps || null;
  const gpsState = await screenState();
  report.gpsScreenState = gpsState;
  check('GPS position received in the app', !!gps, gps || `no position; screen state: ${gpsState}`);
  const screen = await waitFor(`(() => { const t = document.querySelector('.hero-title')?.innerText; return t && !/where do you want|checking/i.test(t) ? t + ' | ' + (document.querySelector('.hero-sub')?.innerText || '') : null; })()`, 60000, () => geo(HOME), 2000);
  report.screen = screen;
  const screenOk = !!screen && /^Free for you here/.test(screen);
  check('Screen shows an answer for my position', screenOk, screenOk ? screen : `${screen || 'nothing shown'}; screen state: ${await screenState()}`);
  screenshot('avenue-louise-216');

  stepTo('free spots');
  // Nearest free spot from Place du Châtelain (live search).
  const spots = await evaluate(`ptTest.freeSpots(${CHATELAIN.lat}, ${CHATELAIN.lon}, 3).then((list) => list.map((s) => s.label + ' ' + Math.round(s.metres) + ' m'))`);
  report.freeSpots = spots;
  check('Nearest free spot found from Place du Châtelain', Array.isArray(spots) && spots.length > 0, (spots || []).join(', '));

  stepTo('parking service');
  // The app starts the foreground service by itself once alerts are on and the location is allowed all the time (a few seconds after launch).
  let services = '';
  for (let i = 0; i < 30 && !/ParkingTrackerLocationService/.test(services); i++) { services = dumpsys(['activity', 'services', PKG]); if (!/ParkingTrackerLocationService/.test(services)) await sleep(2000); }
  report.service = services.split(/\r?\n/).filter((l) => /ParkingTrackerLocationService|isForeground/.test(l)).slice(0, 4).join(' | ');
  check('Foreground service of the parking alerts is running', /ParkingTrackerLocationService/.test(services) && /isForeground=true/.test(services), report.service || 'service not listed');
  const notice = await waitForNotification(/Watching where you park/, 20000);
  check('Permanent notice of the service is shown', !!notice, notice || 'not found in dumpsys notification');
  check('platformStatus reports the service', (await evaluate('ptTest.platformStatus()'))?.service === true);

  stepTo('background parking alert');
  // Drive along Avenue Louise above 20 km/h and stop at number 216, then leave the app and wait for the detector (2 minutes still).
  for (let i = DRIVE_STEPS; i >= 1; i--) { geoMoving({ lat: HOME.lat + i * DRIVE_STEP_DEGREES, lon: HOME.lon }, DRIVE_KNOTS); await sleep(2000); }
  geoMoving(HOME, 0);
  pressHome();
  const alert = await waitForNotification(/android\.title=.*Free for you here/, 240000, () => geoMoving(HOME, 0));
  report.parkingAlert = alert;
  check('Parking alert shown while the app is in the background', !!alert, alert || 'no alert after 4 minutes; notifications: ' + dumpsys(['notification', '--noredact']).split(/\r?\n/).filter((l) => /android\.(title|text)=/.test(l)).slice(0, 6).join(' | '));
  tryAdb(['shell', 'am', 'start', '-n', ACTIVITY]);
  await sleep(3000);
  const carChip = await waitFor(`(() => { const c = document.querySelector('#carChip'); return !!c && !c.classList.contains('hidden'); })()`, 30000);
  check('The parked car was saved by the service and shows as the car chip', !!carChip);

  stepTo('parking timer');
  // A one-minute timer (the form allows 5 at least, the hook does not): the reminder at the end must come while the app is away.
  const started = await evaluate('ptTest.startTimer(1)');
  check('Timer started through the app', started === true, String(started));
  const timerChip = await waitFor(`/left|over/.test(document.querySelector('#carChip')?.innerText || '')`, 15000);
  check('The car chip shows the time left', !!timerChip);
  pressHome();
  const reminder = await waitForNotification(/android\.text=.*Your parking time is over/, 150000);
  report.timerReminder = reminder;
  check('Timer reminder shown at the end of the timer', !!reminder, reminder || 'no reminder after 2.5 minutes');
  tryAdb(['shell', 'am', 'start', '-n', ACTIVITY]);
  await sleep(2000);

  stepTo('errors');
  const exceptions = logs.filter((l) => l.startsWith('EXCEPTION'));
  check('No JavaScript errors', exceptions.length === 0, exceptions.slice(0, 3).join(' | '));
  check('Blazor error UI is hidden', await evaluate(blazorErrorUiHidden));

  return report;
}

let report, fatal = null;
try { report = await main(); } catch (e) {
  fatal = e; report = { checks, logs, fatal: e.message, failedStep: step, appRunning: tryAdb(['shell', 'pidof', PKG]) || 'NOT RUNNING' };
  check(`Test ran to the end (failed at: ${step}; app process: ${report.appRunning})`, false, e.message); screenshot('failure');
}
// Always keep Android's log: app output, crashes (AndroidRuntime) and .NET messages.
// A full log is several megabytes; the default 1 MB output buffer would make the call fail.
const LOG_BUFFER = { maxBuffer: 64 * 1024 * 1024 };
const logcat = tryAdb(['logcat', '-d', '-v', 'time'], LOG_BUFFER);
writeFileSync(join(OUT, 'logcat.txt'), logcat);
const dotnetFailures = logcat.split(/\r?\n/).filter((l) => DOTNET_FAILURE.test(l));
check('No unhandled .NET exception in logcat', dotnetFailures.length === 0, dotnetFailures.slice(0, 2).join(' | '));
const crash = tryAdb(['logcat', '-d', '-b', 'crash', '-v', 'time'], LOG_BUFFER);
writeFileSync(join(OUT, 'crash.txt'), crash);
if (/FATAL EXCEPTION/.test(crash)) {
  const lines = crash.split(/\r?\n/);
  report.crash = lines.slice(0, 25).join('\n');
  check('App did not crash', false, lines.find((l) => /Exception|Error/.test(l)) || 'see crash.txt');
}
try { ws?.close(); } catch { /* closed */ }
writeFileSync(join(OUT, 'report.json'), JSON.stringify(report, null, 2));
const failed = checks.filter((c) => !c.ok);
const md = [`## Android emulator test (.NET MAUI) — ${failed.length ? '❌ ' + failed.length + ' failed' : '✅ all passed'}`,
  '', `Android ${report.device?.android || '?'} · WebView ${report.device?.webview || '?'}`, '',
  '| Check | Result | Detail |', '|---|---|---|',
  ...checks.map((c) => `| ${c.name} | ${c.ok ? '✅' : '❌'} | ${String(c.detail || '').replace(/\|/g, '/').slice(0, 300)} |`)].join('\n');
writeFileSync(join(OUT, 'summary.md'), md + '\n');
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, md + '\n');
console.log('\n' + md);
process.exit(failed.length || fatal ? 1 : 0);
