// Compares two Release APKs of the app against the size and start-up budget of ADR-0018 (at most +8 MB and +300 ms
// over the base build); README.md, "Budget check", describes the method. It runs on a developer
// PC, never in CI; the APKs are the ParkingTracker.apk files of the apks artifacts of two android.yml runs.
//
// Usage:
//   node budget-check.mjs size <base.apk> <head.apk>
//       Writes the sizes and the difference as Markdown to stdout; exit code 1 when the head APK is over budget.
//   node budget-check.mjs cold-start <base.apk> <head.apk> [package]
//       On the connected API 34 emulator, a run of the base, a run of the head, then both again. A run uninstalls the
//       package, installs that APK (adb install -g) and starts it cold six times with `am start -W -S`; the first start
//       (the one after the install) is discarded and the median TotalTime of the others is kept. The second round is
//       reported. Writes Markdown to stdout; exit code 1 when the head is over budget. The package defaults to
//       be.parkingtracker.app; it is uninstalled first, so its data on the emulator is lost.
// Exit code 2 on wrong arguments or an adb failure. Needs Node 22 and ANDROID_HOME (adb-tool.mjs).
import { execFileSync } from 'node:child_process';
import { statSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { adbPath, apkArgument } from './adb-tool.mjs';

export const SIZE_BUDGET_BYTES = 8 * 1024 * 1024;
export const START_BUDGET_MS = 300;
export const STARTS_PER_RUN = 6;
export const ROUNDS = 2;
const DEFAULT_PACKAGE = 'be.parkingtracker.app';
const PACKAGE_PATTERN = /^[a-zA-Z][\w]*(\.[a-zA-Z][\w]*)+$/;

// The middle value of a list of numbers, or the mean of the two middle values for an even count.
export function median(values) {
  if (values.length === 0) {
    throw new Error('The median of an empty list is undefined.');
  }
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

// The TotalTime line of `am start -W`, in milliseconds; an error when the start did not report one.
export function parseTotalTime(output) {
  const match = /^TotalTime:\s*(\d+)\s*$/m.exec(output);
  if (!match) {
    throw new Error(`am start -W reported no TotalTime:\n${output}`);
  }
  return Number(match[1]);
}

// The start-up time of one run: the first (warming) start is discarded, the median of the others is kept.
export function runResult(times) {
  if (times.length < 2) {
    throw new Error('A run needs at least two starts, the first being discarded.');
  }
  return median(times.slice(1));
}

// Bytes as megabytes (1 MB = 1,048,576 bytes) with two decimals, the unit of the S3 spike.
export function megabytes(bytes) {
  return (bytes / (1024 * 1024)).toFixed(2);
}

// The size verdict as Markdown lines plus whether the head APK stays within the budget.
export function sizeReport(baseBytes, headBytes) {
  const difference = headBytes - baseBytes;
  const withinBudget = difference <= SIZE_BUDGET_BYTES;
  const sign = difference >= 0 ? '+' : '-';
  const lines = [
    '| Build | Release APK | Difference | Budget (8 MB) |',
    '| --- | --- | --- | --- |',
    `| Base | ${megabytes(baseBytes)} MB | - | - |`,
    `| Head | ${megabytes(headBytes)} MB | ${sign}${megabytes(Math.abs(difference))} MB | ${withinBudget ? 'met' : 'exceeded'} |`,
  ];
  return { lines, withinBudget };
}

// The start-up verdict as Markdown lines plus whether the head stays within the budget; rounds holds the run results
// per round as { base, head }, and the last round is the one reported.
export function coldStartReport(rounds) {
  const reported = rounds.at(-1);
  const difference = reported.head - reported.base;
  const withinBudget = difference <= START_BUDGET_MS;
  const lines = [
    '| Build | Cold start (median) | Difference | Budget (300 ms) |',
    '| --- | --- | --- | --- |',
    `| Base | ${reported.base} ms | - | - |`,
    `| Head | ${reported.head} ms | ${difference >= 0 ? '+' : ''}${difference} ms | ${withinBudget ? 'met' : 'exceeded'} |`,
    '',
    `Every round (median of starts 2 to ${STARTS_PER_RUN} of each run): ` +
      rounds.map((round, index) => `round ${index + 1} base ${round.base} ms, head ${round.head} ms`).join('; ') + '.',
  ];
  return { lines, withinBudget };
}

// A package name from the command line, checked so that adb cannot read it as an option or a second command.
function packageArgument(value, fallback, label) {
  const packageName = value ?? fallback;
  if (!PACKAGE_PATTERN.test(packageName)) {
    throw new Error(`${label} must be an Android package name, not ${packageName}.`);
  }
  return packageName;
}

function adb(args) {
  return execFileSync(adbPath(), args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
}

// The launcher activity of an installed package, as package/class for am start.
function launcherActivity(packageName) {
  const lines = adb(['shell', 'cmd', 'package', 'resolve-activity', '--brief', packageName]).trim().split(/\r?\n/);
  const component = lines.at(-1).trim();
  if (!component.includes('/')) {
    throw new Error(`No launcher activity found for ${packageName}: ${lines.join(' ')}`);
  }
  return component;
}

// One run: a fresh install of the APK, then the cold starts; returns the median of all but the first start.
function coldStartRun(apk, packageName) {
  try {
    adb(['uninstall', packageName]);
  } catch {
    // Not installed yet: nothing to remove.
  }
  adb(['install', '-g', apk]);
  const component = launcherActivity(packageName);
  const times = [];
  for (let start = 0; start < STARTS_PER_RUN; start += 1) {
    times.push(parseTotalTime(adb(['shell', 'am', 'start', '-W', '-S', '-n', component])));
  }
  adb(['shell', 'am', 'force-stop', packageName]);
  console.error(`${apk}: ${times.join(', ')} ms`);
  return runResult(times);
}

function measureColdStart(baseApk, headApk, packageName) {
  const rounds = [];
  for (let round = 0; round < ROUNDS; round += 1) {
    rounds.push({ base: coldStartRun(baseApk, packageName), head: coldStartRun(headApk, packageName) });
  }
  return coldStartReport(rounds);
}

function main(argv) {
  const [command, base, head, packageName] = argv;
  const baseApk = apkArgument(base, 'The base APK');
  const headApk = apkArgument(head, 'The head APK');
  let report;
  if (command === 'size') {
    report = sizeReport(statSync(baseApk).size, statSync(headApk).size);
  } else if (command === 'cold-start') {
    report = measureColdStart(baseApk, headApk, packageArgument(packageName, DEFAULT_PACKAGE, 'The package'));
  } else {
    throw new Error('The first argument must be size or cold-start.');
  }
  console.log(report.lines.join('\n'));
  return report.withinBudget ? 0 : 1;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(`::error::${error.message}`);
    process.exitCode = 2;
  }
}
