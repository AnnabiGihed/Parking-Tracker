// Runs emulator-test.mjs, and runs it once more when Android itself restarted during the first run.
//
// Usage (same as emulator-test.mjs, from the repository root):  node test/ParkingTracker.E2E.Tests/run-emulator-test.mjs
// Output: emulator-report/ of the run that counts; after a retry, the first run's report is kept in emulator-report/attempt-1/.
//
// On a freshly booted CI emulator, Google Play services updates its own modules a few minutes after start. When that update
// lands while the app is in the background, the fused location provider disappears and the system's GNSS code can deadlock;
// the system watchdog then kills the whole Android framework, the app with it, and the parking alert never comes. That is the
// emulator, not the app, so the run is repeated once on the restarted system, with the app installed afresh. Any other
// failure, and a second restart, still fail the check.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { adbPath } from './adb-tool.mjs';

const OUT = 'emulator-report';
const PKG = 'be.parkingtracker.app';
const TEST = join(dirname(fileURLToPath(import.meta.url)), 'emulator-test.mjs');
// What the system watchdog writes just before it kills the Android framework.
const SYSTEM_RESTART = 'WATCHDOG KILLING SYSTEM PROCESS';
const BOOT_WAIT_MS = 300000;

function runTest() {
  return spawnSync(process.execPath, [TEST], { stdio: 'inherit' }).status ?? 1;
}

function androidRestarted() {
  const logcat = join(OUT, 'logcat.txt');
  return existsSync(logcat) && readFileSync(logcat, 'utf8').includes(SYSTEM_RESTART);
}

function adb(args) {
  return spawnSync(adbPath(), args, { encoding: 'utf8', timeout: 60000 }).stdout?.trim() ?? '';
}

// Waits until the restarted framework has booted again and its package service answers (sys.boot_completed can survive a
// framework restart, so it is not enough on its own).
function waitForBoot() {
  const until = Date.now() + BOOT_WAIT_MS;
  adb(['wait-for-device']);
  while (Date.now() < until) {
    if (adb(['shell', 'getprop', 'sys.boot_completed']) === '1' && adb(['shell', 'pm', 'path', 'android']).startsWith('package:')) return true;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 5000);
  }
  return false;
}

// Moves the first run's files into attempt-1/, so that the second run writes a clean report next to them.
function keepFirstReport() {
  const kept = join(OUT, 'attempt-1');
  mkdirSync(kept, { recursive: true });
  for (const name of readdirSync(OUT)) {
    if (name !== 'attempt-1') renameSync(join(OUT, name), join(kept, name));
  }
}

// A report left by an earlier local run must not make this run look like a restart.
rmSync(OUT, { recursive: true, force: true });
let status = runTest();
if (status !== 0 && androidRestarted()) {
  console.log('RETRY emulator test: the Android system restarted during the run (system watchdog); running it once more');
  keepFirstReport();
  if (!waitForBoot()) {
    console.log(`FAIL  Android did not finish booting within ${BOOT_WAIT_MS / 1000} s after the restart`);
    process.exit(1);
  }
  adb(['uninstall', PKG]);
  status = runTest();
}
process.exit(status);
