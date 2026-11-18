// Proves that the saved data of the Capacitor version survives the update to the .NET MAUI version (ADR-0004, ADR-0008).
//
// Usage (emulator booted, adb on PATH):
//   node test/ParkingTracker.E2E.Tests/upgrade-test.mjs path/to/capacitor-debug.apk path/to/maui-test.apk
// Both APKs must carry the same signing key (the workflow re-signs them with a throwaway key) and the MAUI versionCode must be higher.
// The Capacitor APK must be a debug build (web view debugging on); the MAUI APK is the test-hooks build, which has window.ptTest.
// Output: upgrade-report/ (report.json, summary.md, logcat.txt, screenshot-*.png) and exit code 1 on failure.
//
// Steps: install the Capacitor build, launch it, write pt.card, pt.car, pt.layers, pt.bgAlerts and pt.permSnooze into the localStorage
// of https://localhost through the devtools protocol, wait until the web view flushed them to disk, force-stop, launch the old app once
// more and wait (so that LevelDB recovery or compaction writes table files, as for a real user who used the app for a while),
// force-stop again, record the files of Local Storage/leveldb, install the MAUI build over it with `adb install -r`, launch it, and
// read the card, the car and the migration marker (with the reader that was used) through window.ptTest. The welcome dialog, which
// shows when there is no card, must not be shown. See docs/architecture.md section 6.3.
import { mkdirSync, writeFileSync, appendFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import { connectWebView, tryAdb } from './webview-cdp.mjs';
import { adbPath, apkArgument } from './adb-tool.mjs';
import { appFailureLines } from './app-crashes.mjs';

const [CAPACITOR_APK, MAUI_APK] = process.argv.slice(2);
const PKG = 'be.parkingtracker.app';
const CAPACITOR_ACTIVITY = `${PKG}/.MainActivity`;
const MAUI_ACTIVITY = `${PKG}/${PKG}.MainActivity`; // the Java name is fixed by [Activity(Name = ...)], see ADR-0001
const OUT = 'upgrade-report';
const WEB_VIEW_DIRECTORY = 'app_webview/Default'; // inside the data directory of the app
// Chromium writes localStorage to disk a few seconds after the last change, so the test waits before it stops the old app.
const MIN_FLUSH_WAIT_MS = 12000;
const MAX_FLUSH_WAIT_MS = 60000;
// How long the old app runs the second time; LevelDB replays its log and may write a table file when it opens the database.
const RELAUNCH_WAIT_MS = 15000;
const LEVELDB_DIRECTORY = 'app_webview/Default/Local Storage/leveldb';
const KNOWN_READERS = ['leveldb', 'webview'];

// What the old app wrote, in the JSON shapes of app.js (ADR-0004).
const OLD_CARD = { number: 'UPG-1', plate: '1UPG234', municipality: 'bruxelles', sector: 'louise', from: '2026-01-01', to: '2099-12-31', tolerances: ['BXL_LIM_IXL_LOUISE'] };
const OLD_CAR = { lat: 50.82655, lon: 4.36505, t: 1760000000000, status: 'Parked', where: 'Avenue Louise 216' };
const OLD_LAYERS = { streets: true, spaces: true, works: false, carparks: true, chargers: false };
const OLD_VALUES = {
  'pt.card': JSON.stringify(OLD_CARD),
  'pt.car': JSON.stringify(OLD_CAR),
  'pt.layers': JSON.stringify(OLD_LAYERS),
  'pt.bgAlerts': 'false',
  'pt.permSnooze': String(Date.now() + 86400000),
};

if (!CAPACITOR_APK || !MAUI_APK) {
  console.error('Usage: node upgrade-test.mjs path/to/capacitor-debug.apk path/to/maui-test.apk');
  process.exit(2);
}

mkdirSync(OUT, { recursive: true });
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const checks = [];
let step = 'start';
const stepTo = (name) => { step = name; console.log(`--- ${name}`); };
const check = (name, ok, detail) => { checks.push({ name, ok: !!ok, detail }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`); };
const screenshot = (name) => {
  const result = spawnSync(adbPath(), ['exec-out', 'screencap', '-p'], { maxBuffer: 50 * 1024 * 1024 });
  if (result.status === 0) writeFileSync(join(OUT, `screenshot-${name}.png`), result.stdout);
};
// The versionCode of the installed package, from dumpsys.
const installedVersionCode = () => Number(tryAdb(['shell', 'dumpsys', 'package', PKG], { maxBuffer: 16 * 1024 * 1024 }).match(/versionCode=(\d+)/)?.[1] ?? 0);

// Waits until the web view data of the app on disk mentions the card key, which proves the localStorage write reached the files.
// run-as only works for a debuggable app, which the Capacitor debug build is; when it does not answer the minimum wait still applies.
async function waitForFlush() {
  const start = Date.now();
  await sleep(MIN_FLUSH_WAIT_MS);
  while (Date.now() - start < MAX_FLUSH_WAIT_MS) {
    const found = tryAdb(['shell', 'run-as', PKG, 'grep', '-rlsa', 'pt.card', WEB_VIEW_DIRECTORY]);
    if (found.includes('Local Storage')) return found.split(/\r?\n/)[0];
    await sleep(3000);
  }
  return null;
}

// Lists the files of the old web view's LevelDB directory (name, size, time); run-as works for the debug build only.
function listLevelDbFiles() {
  return tryAdb(['shell', `run-as ${PKG} ls -la '${LEVELDB_DIRECTORY}'`]);
}

// Starts the old app once more and stops it after a wait, so that the files on disk are those of an app that was used before.
async function relaunchOldApp(report) {
  stepTo('relaunch the Capacitor build once');
  report.leveldbFilesBeforeRelaunch = listLevelDbFiles();
  tryAdb(['shell', 'am', 'start', '-n', CAPACITOR_ACTIVITY]);
  await sleep(RELAUNCH_WAIT_MS);
  tryAdb(['shell', 'am', 'force-stop', PKG]);
  await sleep(2000);
  report.leveldbFiles = listLevelDbFiles();
  console.log(`LevelDB files before the update:\n${report.leveldbFiles}`);
  check('The old web view left LevelDB files', /\.(log|ldb)\b/.test(report.leveldbFiles), report.leveldbFiles.split(/\r?\n/).map((l) => l.split(/\s+/).pop()).join(' '));
}

// Installs the old build, stores the data the old app would have saved and stops the app.
async function prepareOldInstall(report) {
  stepTo('install the Capacitor build');
  console.log(tryAdb(['install', '-r', '-g', apkArgument(CAPACITOR_APK, 'The Capacitor APK')]));
  report.capacitorVersionCode = installedVersionCode();
  check('Capacitor build installed', report.capacitorVersionCode > 0, `versionCode ${report.capacitorVersionCode}`);
  tryAdb(['logcat', '-c']);
  tryAdb(['shell', 'am', 'start', '-n', CAPACITOR_ACTIVITY]);
  await sleep(8000);

  stepTo('store data in the Capacitor web view');
  const old = await connectWebView(PKG);
  const origin = await old.waitFor('document.readyState === \'complete\' && location.origin', 60000);
  check('Capacitor app runs in the origin https://localhost', origin === 'https://localhost', String(origin));
  const stored = await old.evaluate(`(() => { const values = ${JSON.stringify(OLD_VALUES)}; for (const k of Object.keys(values)) localStorage.setItem(k, values[k]); return Object.keys(localStorage).filter((k) => k.startsWith('pt.')).sort(); })()`);
  check('The old values are in localStorage', Object.keys(OLD_VALUES).every((k) => stored.includes(k)), JSON.stringify(stored));
  old.close();

  stepTo('wait for the web view to write the files');
  const flushed = await waitForFlush();
  report.flushedTo = flushed;
  console.log(`localStorage on disk: ${flushed ?? 'not confirmed (continuing after the maximum wait)'}`);
  tryAdb(['shell', 'am', 'force-stop', PKG]);
  await sleep(2000);
  await relaunchOldApp(report);
}

// Installs the MAUI build over the old one, launches it and returns its web view.
async function startNewVersion(report) {
  stepTo('install the MAUI build over the Capacitor build');
  const installed = tryAdb(['install', '-r', apkArgument(MAUI_APK, 'The MAUI APK')]);
  console.log(installed);
  report.mauiVersionCode = installedVersionCode();
  check('MAUI build installed over the Capacitor build with install -r', /Success/.test(installed), installed.slice(0, 200));
  check('The MAUI versionCode is higher', report.mauiVersionCode > report.capacitorVersionCode, `${report.capacitorVersionCode} -> ${report.mauiVersionCode}`);
  tryAdb(['logcat', '-c']);
  tryAdb(['shell', 'am', 'start', '-n', MAUI_ACTIVITY]);
  await sleep(8000);

  stepTo('connect to the MAUI web view');
  const app = await connectWebView(PKG);
  const ready = await app.waitFor('!!window.ptTest && !!document.querySelector(\'.hero-title\')', 120000);
  check('The MAUI app runs with the test hooks', !!ready);
  return app;
}

// Checks that the old values came over and the app behaves as for a user who keeps the card.
async function checkMigratedData(app, report) {
  stepTo('check the migrated data');
  const card = JSON.parse((await app.evaluate('ptTest.getCard()')) || 'null');
  report.card = card;
  check('The card came over', card?.plate === OLD_CARD.plate && card?.number === OLD_CARD.number && card?.sector === OLD_CARD.sector && card?.municipality === OLD_CARD.municipality
    && card?.from === OLD_CARD.from && card?.to === OLD_CARD.to && card?.tolerances?.[0] === OLD_CARD.tolerances[0], JSON.stringify(card));
  const car = JSON.parse((await app.evaluate('ptTest.getCar()')) || 'null');
  report.car = car;
  check('The parked car came over', car?.lat === OLD_CAR.lat && car?.lon === OLD_CAR.lon && car?.t === OLD_CAR.t && car?.where === OLD_CAR.where, JSON.stringify(car));
  const marker = JSON.parse((await app.evaluate('ptTest.migration()')) || 'null');
  report.marker = marker;
  check('The migration marker pt.migration is set', marker?.version === 1 && marker?.outcome === 'migrated' && marker?.found?.includes('pt.card') && marker?.found?.includes('pt.car'), JSON.stringify(marker));
  report.reader = marker?.reader;
  check('The migration marker names the reader that was used', KNOWN_READERS.includes(marker?.reader), `reader ${marker?.reader}`);
  check('The alerts switch and the layers were copied', ['pt.bgAlerts', 'pt.layers', 'pt.permSnooze'].every((k) => marker?.found?.includes(k)), JSON.stringify(marker?.found));
  // The welcome dialog opens on start when there is no card; give it time to appear before concluding it did not.
  await sleep(5000);
  const welcome = await app.evaluate('!!document.querySelector(\'#welcomeDialog\')');
  check('The welcome dialog is not shown for a migrated card', welcome === false);
  const cardLine = await app.evaluate('document.querySelector(\'#cardLine\')?.innerText || document.body.innerText.slice(0, 200)');
  report.screenText = cardLine;
  screenshot('after-upgrade');
}

async function main() {
  const report = { checks };
  report.device = { android: tryAdb(['shell', 'getprop', 'ro.build.version.release']) };
  await prepareOldInstall(report);
  const app = await startNewVersion(report);
  try {
    await checkMigratedData(app, report);
    const exceptions = app.logs.filter((line) => line.startsWith('EXCEPTION'));
    check('No JavaScript errors', exceptions.length === 0, exceptions.slice(0, 3).join(' | '));
  } finally {
    app.close();
  }
  return report;
}

let report;
let fatal = null;
try { report = await main(); } catch (error) {
  fatal = error;
  report = { checks, fatal: error.message, failedStep: step, appRunning: tryAdb(['shell', 'pidof', PKG]) || 'NOT RUNNING' };
  check(`Test ran to the end (failed at: ${step}; app process: ${report.appRunning})`, false, error.message);
  screenshot('failure');
}
const logcat = tryAdb(['logcat', '-d', '-v', 'time'], { maxBuffer: 64 * 1024 * 1024 });
writeFileSync(join(OUT, 'logcat.txt'), logcat);
const dotnetFailures = appFailureLines(logcat, PKG);
check('No unhandled .NET exception in logcat', dotnetFailures.length === 0, dotnetFailures.slice(0, 2).join(' | '));
writeFileSync(join(OUT, 'report.json'), JSON.stringify(report, null, 2));
const failed = checks.filter((c) => !c.ok);
const md = [`## Upgrade test (Capacitor to .NET MAUI) — ${failed.length ? '❌ ' + failed.length + ' failed' : '✅ all passed'}`,
  '', `Android ${report.device?.android || '?'} · versionCode ${report.capacitorVersionCode ?? '?'} → ${report.mauiVersionCode ?? '?'} · migration reader ${report.reader ?? '?'}`,
  '', 'LevelDB files of the old app before the update:', '', '```', String(report.leveldbFiles ?? 'not listed'), '```', '',
  '| Check | Result | Detail |', '|---|---|---|',
  ...checks.map((c) => `| ${c.name} | ${c.ok ? '✅' : '❌'} | ${String(c.detail || '').replaceAll('|', '/').slice(0, 300)} |`)].join('\n');
writeFileSync(join(OUT, 'summary.md'), md + '\n');
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, md + '\n');
console.log('\n' + md);
process.exit(failed.length || fatal ? 1 : 0);
