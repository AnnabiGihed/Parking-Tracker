// Runs the Android app in an emulator (GitHub Actions) and checks what it really does on Android:
// native HTTP, permissions, GPS, the official-list check and the answers at known places.
//
// Usage (emulator already booted, adb on PATH):  node tools/emulator-test.mjs path/to/app-debug.apk
// Output: emulator-report/ (report.json, summary.md, screenshot-*.png, logcat.txt) and exit code 1 on failure.
//
// The test build is a debug build of the same code: its WebView can be inspected through Chrome DevTools
// Protocol, which lets the script add a test card and read the app's answers.
import { execSync, spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync, appendFileSync } from 'node:fs';
import { join } from 'node:path';

const APK = process.argv[2];
const PKG = 'be.parkingtracker.app';
const OUT = 'emulator-report';
const HOME = { name: 'Avenue Louise 216', lat: 50.82655, lon: 4.36505, expect: ['FREE ✓'] };
const CHATELAIN = { name: 'Place du Châtelain (Ixelles)', lat: 50.82480, lon: 4.36040, expect: ['PAY', 'FREE NOW'] };
const TEST_CARD = { number: 'TEST', plate: '1TST000', sector: 'louise', from: '2026-01-01', to: '2099-12-31', tolerances: [] };

mkdirSync(OUT, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const adb = (args, opts = {}) => execSync(`adb ${args}`, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...opts }).trim();
const tryAdb = (args) => { try { return adb(args); } catch (e) { return `ERR ${e.message.split('\n')[0]}`; } };
const geo = (p) => tryAdb(`emu geo fix ${p.lon} ${p.lat}`); // longitude first
const screenshot = (name) => {
  const r = spawnSync('adb', ['exec-out', 'screencap', '-p'], { maxBuffer: 50 * 1024 * 1024 });
  if (r.status === 0) writeFileSync(join(OUT, `screenshot-${name}.png`), r.stdout);
};

const checks = [];
const check = (name, ok, detail) => { checks.push({ name, ok: !!ok, detail }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`); };

// ---------- Chrome DevTools Protocol client (Node 22 has a global WebSocket) ----------
async function connectWebView() {
  for (let i = 0; i < 60; i++) {
    const pid = tryAdb(`shell pidof ${PKG}`);
    const sockets = tryAdb('shell cat /proc/net/unix');
    const m = sockets.match(new RegExp(`@?(webview_devtools_remote_${pid.split(/\s+/)[0]})`));
    if (m) {
      tryAdb('forward --remove-all');
      adb(`forward tcp:9222 localabstract:${m[1]}`);
      try {
        const pages = await (await fetch('http://127.0.0.1:9222/json')).json();
        const page = pages.find((p) => p.type === 'page' && p.webSocketDebuggerUrl);
        if (page) return page.webSocketDebuggerUrl;
      } catch { /* not ready */ }
    }
    await sleep(2000);
  }
  throw new Error('WebView devtools socket not found (is this a debug build?)');
}
const logs = [];
let ws, nextId = 0;
const pending = new Map();
function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++nextId;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
    setTimeout(() => { if (pending.has(id)) { pending.delete(id); reject(new Error(`CDP ${method} timed out`)); } }, 90000);
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
}
const waitFor = async (expr, ms = 60000) => {
  const end = Date.now() + ms;
  while (Date.now() < end) { try { const v = await evaluate(expr); if (v) return v; } catch { /* page reloading */ } await sleep(1000); }
  return null;
};

// ---------- the test ----------
async function main() {
  const report = { device: {}, checks, scenarios: {}, logs };
  report.device.android = tryAdb('shell getprop ro.build.version.release');
  report.device.webview = tryAdb('shell dumpsys package com.google.android.webview').match(/versionName=(\S+)/)?.[1] || 'unknown';

  // Install and grant what a user grants on first launch ("Allow all the time", notifications).
  console.log(adb(`install -r -g "${APK}"`));
  for (const p of ['ACCESS_FINE_LOCATION', 'ACCESS_COARSE_LOCATION', 'ACCESS_BACKGROUND_LOCATION', 'POST_NOTIFICATIONS']) tryAdb(`shell pm grant ${PKG} android.permission.${p}`);
  tryAdb('shell settings put secure location_mode 3');
  geo(HOME);

  // Launch, add the test card, restart.
  tryAdb(`shell am start -n ${PKG}/.MainActivity`);
  await cdp();
  await waitFor('!!window.PT && !!window.__pt', 90000);
  await evaluate(`localStorage.setItem('pt.card', ${JSON.stringify(JSON.stringify(TEST_CARD))}); localStorage.setItem('pt.permSnooze', String(Date.now() + 864e5)); true`);
  await evaluate('location.reload(); true').catch(() => {});
  await sleep(3000);
  await waitFor('!!window.PT && !!window.__pt && !!window.__pt.state.sector', 90000);
  check('App runs inside Capacitor (native)', await evaluate('!!window.Capacitor && window.PT.IN_APP'));

  // Native plugins available?
  const plugins = await evaluate(`(async () => {
    const P = window.Capacitor.Plugins, out = {};
    out.CapacitorHttp = !!P.CapacitorHttp; out.LocalNotifications = !!P.LocalNotifications;
    out.BackgroundGeolocation = !!(P.BackgroundGeolocation || window.Capacitor.registerPlugin('BackgroundGeolocation'));
    try { out.LocationAccess = await (P.LocationAccess || window.Capacitor.registerPlugin('LocationAccess')).status(); } catch (e) { out.LocationAccess = 'ERROR ' + e.message; }
    return out; })()`);
  report.plugins = plugins;
  check('Native plugins present', plugins.CapacitorHttp && plugins.LocalNotifications && plugins.BackgroundGeolocation && typeof plugins.LocationAccess === 'object',
    JSON.stringify(plugins));
  check('Location allowed all the time', plugins.LocationAccess?.background === true, JSON.stringify(plugins.LocationAccess));

  // The official City list check through native HTTP (the bug of v1.0.7).
  const list = await evaluate(`(async () => { const c = await window.__pt.ctx(); return { ...window.__pt.state.list, problem: c.listProblem }; })()`);
  report.list = list;
  check('City sector list confirmed unchanged (native HTTP)', list?.status === 'ok', JSON.stringify(list));

  // Live answers at known places, through the app's own decision code.
  for (const s of [HOME, CHATELAIN]) {
    const r = await evaluate(`(async () => { const r = await window.PT.decide(await window.__pt.ctx(), ${s.lat}, ${s.lon}, 5);
      return { title: r.title, status: r.status, detail: r.detail.replace(/<[^>]+>/g, ''), tariff: r.tariff || null, notes: (r.notes || []).map((n) => n.replace(/<[^>]+>/g, '')) }; })()`);
    report.scenarios[s.name] = r;
    check(`Answer at ${s.name}`, s.expect.includes(r?.title), `${r?.title} — ${r?.detail}`);
  }

  // Real GPS path: the emulator's GPS is set to Avenue Louise 216; the screen must show the answer.
  for (let i = 0; i < 12; i++) { geo(HOME); await sleep(1500); }
  const me = await waitFor('window.__pt.state.me && JSON.stringify(window.__pt.state.me)', 60000);
  report.gps = me ? JSON.parse(me) : null;
  check('GPS position received in the app', !!me, me || 'no position');
  const screen = await waitFor(`(() => { const t = document.querySelector('.hero-title')?.innerText; return t && !/where do you want|checking/i.test(t) ? t + ' | ' + (document.querySelector('.hero-sub')?.innerText || '') : null; })()`, 60000);
  report.screen = screen;
  check('Screen shows an answer for my position', !!screen && /^Free for you here/.test(screen), screen || 'nothing shown');
  screenshot('avenue-louise-216');

  // Nearest free spot from Place du Châtelain (live search).
  const spots = await evaluate(`(async () => (await window.PT.liveFreeSpots(await window.__pt.ctx(), ${CHATELAIN.lat}, ${CHATELAIN.lon}, 3)).map((s) => s.label + ' ' + Math.round(s.m) + ' m'))()`);
  report.freeSpots = spots;
  check('Nearest free spot found from Place du Châtelain', Array.isArray(spots) && spots.length > 0, (spots || []).join(', '));

  const exceptions = logs.filter((l) => l.startsWith('EXCEPTION'));
  check('No JavaScript errors', exceptions.length === 0, exceptions.slice(0, 3).join(' | '));

  writeFileSync(join(OUT, 'logcat.txt'), tryAdb('logcat -d -v time Capacitor:V Capacitor/Console:V chromium:V AndroidRuntime:E *:S'));
  return report;
}

let report, fatal = null;
try { report = await main(); } catch (e) { fatal = e; report = { checks, logs, fatal: e.message }; check('Test ran to the end', false, e.message); screenshot('failure'); }
try { ws?.close(); } catch { /* closed */ }
writeFileSync(join(OUT, 'report.json'), JSON.stringify(report, null, 2));
const failed = checks.filter((c) => !c.ok);
const md = [`## Android emulator test — ${failed.length ? '❌ ' + failed.length + ' failed' : '✅ all passed'}`,
  '', `Android ${report.device?.android || '?'} · WebView ${report.device?.webview || '?'}`, '',
  '| Check | Result | Detail |', '|---|---|---|',
  ...checks.map((c) => `| ${c.name} | ${c.ok ? '✅' : '❌'} | ${String(c.detail || '').replace(/\|/g, '/').slice(0, 300)} |`)].join('\n');
writeFileSync(join(OUT, 'summary.md'), md + '\n');
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, md + '\n');
console.log('\n' + md);
process.exit(failed.length || fatal ? 1 : 0);
