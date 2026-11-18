// Finds the failures that belong to the app in an Android log, so a crash of another process on the emulator
// (System UI, the phone service) is reported in the log files but does not fail a check about the app.

// .NET and Blazor messages that only the app writes.
const MANAGED_FAILURE = /FATAL UNHANDLED EXCEPTION|Unhandled managed exception|Unhandled exception rendering component/i;
// A Java crash: the line after "FATAL EXCEPTION" names the process.
const CRASH_PROCESS = /AndroidRuntime\(\s*(\d+)\):\s*Process:\s*(\S+), PID/;
const CRASH_START = /AndroidRuntime\(\s*(\d+)\).*FATAL EXCEPTION/;

// The PIDs of the app's processes that crashed, read from the "Process: <name>, PID: <pid>" lines.
function crashedAppPids(lines, packageName) {
  const pids = new Set();
  for (const line of lines) {
    const match = CRASH_PROCESS.exec(line);
    if (match && (match[2] === packageName || match[2].startsWith(`${packageName}:`))) pids.add(match[1]);
  }
  return pids;
}

// The log lines that show the app failing: managed failures, and Java crashes of the app's own processes.
export function appFailureLines(log, packageName) {
  const lines = String(log ?? '').split(/\r?\n/);
  const pids = crashedAppPids(lines, packageName);
  return lines.filter((line) => {
    if (MANAGED_FAILURE.test(line)) return true;
    const start = CRASH_START.exec(line);
    return !!start && pids.has(start[1]);
  });
}
