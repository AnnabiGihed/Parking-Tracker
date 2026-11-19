// Unit tests of the pure parts of budget-check.mjs (node --test); the adb part needs an emulator and is not covered.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  SIZE_BUDGET_BYTES,
  START_BUDGET_MS,
  coldStartReport,
  median,
  megabytes,
  parseTotalTime,
  runResult,
  sizeReport,
} from './budget-check.mjs';

test('median takes the middle value of an odd count and the mean of the two middle values of an even count', () => {
  assert.equal(median([2100, 1990, 2050, 2010, 2200]), 2050);
  assert.equal(median([4, 1, 3, 2]), 2.5);
  assert.throws(() => median([]));
});

test('parseTotalTime reads the TotalTime line and refuses output without one', () => {
  const output = 'Starting: Intent { cmp=be.parkingtracker.app/.MainActivity }\nStatus: ok\nLaunchState: COLD\n' +
    'Activity: be.parkingtracker.app/.MainActivity\nTotalTime: 2003\nWaitTime: 2010\nComplete\n';
  assert.equal(parseTotalTime(output), 2003);
  assert.equal(parseTotalTime(output.replace(/\n/g, '\r\n')), 2003);
  assert.throws(() => parseTotalTime('Error: Activity not started'));
});

test('runResult discards the first start', () => {
  assert.equal(runResult([9000, 2000, 2100, 1900, 2050, 2010]), 2010);
  assert.throws(() => runResult([2000]));
});

test('megabytes uses 1 MB = 1,048,576 bytes', () => {
  assert.equal(megabytes(1024 * 1024 * 2.5), '2.50');
});

test('sizeReport meets the budget at exactly 8 MB more and exceeds it one byte above', () => {
  const base = 70 * 1024 * 1024;
  assert.equal(sizeReport(base, base + SIZE_BUDGET_BYTES).withinBudget, true);
  const over = sizeReport(base, base + SIZE_BUDGET_BYTES + 1);
  assert.equal(over.withinBudget, false);
  assert.match(over.lines.at(-1), /exceeded/);
  assert.match(sizeReport(base, base - 1024 * 1024).lines.at(-1), /-1\.00 MB/);
});

test('coldStartReport judges the last round only', () => {
  const met = coldStartReport([{ base: 2000, head: 2500 }, { base: 2000, head: 2000 + START_BUDGET_MS }]);
  assert.equal(met.withinBudget, true);
  assert.match(met.lines[3], /\+300 ms \| met/);
  const exceeded = coldStartReport([{ base: 2000, head: 2000 }, { base: 2000, head: 2000 + START_BUDGET_MS + 1 }]);
  assert.equal(exceeded.withinBudget, false);
});
