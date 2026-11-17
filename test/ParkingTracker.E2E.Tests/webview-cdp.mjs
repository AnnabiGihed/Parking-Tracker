// A small Chrome DevTools Protocol client for the web view of an Android app (Node 22 built-ins only: global fetch and WebSocket).
// upgrade-test.mjs uses it twice in one run, once for the Capacitor build and once for the MAUI build, which is why the state lives in an
// object that is created per connection. All adb calls take argument arrays, never a shell string.
import { execFileSync } from 'node:child_process';
import { adbPath } from './adb-tool.mjs';

const ADB = adbPath();

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const CALL_TIMEOUT_MS = 45000;
const FORWARD_PORT = 9222;

// Runs adb with an argument array; returns the trimmed output or 'ERR <first line>' when adb failed.
export function tryAdb(args, options = {}) {
  try {
    return execFileSync(ADB, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...options }).trim();
  } catch (error) {
    return `ERR ${error.message.split('\n')[0]}`;
  }
}

// Finds the devtools socket of the running app, forwards it to a local port and returns the WebSocket URL of its page.
async function findPageUrl(pkg, attempts) {
  for (let i = 0; i < attempts; i++) {
    const pid = tryAdb(['shell', 'pidof', pkg]).split(/\s+/)[0];
    const sockets = tryAdb(['shell', 'cat', '/proc/net/unix']);
    const match = pid && sockets.match(new RegExp(`@?(webview_devtools_remote_${pid})`));
    if (match) {
      tryAdb(['forward', '--remove-all']);
      tryAdb(['forward', `tcp:${FORWARD_PORT}`, `localabstract:${match[1]}`]);
      try {
        const pages = await (await fetch(`http://127.0.0.1:${FORWARD_PORT}/json`)).json();
        const page = pages.find((p) => p.type === 'page' && p.webSocketDebuggerUrl);
        if (page) return page.webSocketDebuggerUrl;
      } catch { /* the page is not ready yet */ }
    }
    await sleep(2000);
  }
  throw new Error(`The web view of ${pkg} has no devtools socket (is web view debugging on in this build?)`);
}

// Connects to the web view of the running app and returns { evaluate, waitFor, logs, close }.
export async function connectWebView(pkg, attempts = 60) {
  const socket = new WebSocket(await findPageUrl(pkg, attempts));
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  const logs = [];
  const pending = new Map();
  let nextId = 0;
  socket.onmessage = (message) => {
    const data = JSON.parse(message.data);
    if (data.id && pending.has(data.id)) {
      const call = pending.get(data.id);
      pending.delete(data.id);
      if (data.error) call.reject(new Error(data.error.message)); else call.resolve(data.result);
      return;
    }
    if (data.method === 'Runtime.exceptionThrown') logs.push(`EXCEPTION: ${data.params.exceptionDetails.exception?.description || data.params.exceptionDetails.text}`);
  };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++nextId;
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
    setTimeout(() => { if (pending.delete(id)) reject(new Error(`CDP ${method} timed out`)); }, CALL_TIMEOUT_MS);
  });
  await send('Runtime.enable');

  // Evaluates an expression in the page; a promise is awaited and the value is returned by value.
  const evaluate = async (expression) => {
    const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
    return result.result?.value;
  };
  // Evaluates an expression until it is truthy or the time is up; returns the value or null.
  const waitFor = async (expression, ms = 60000) => {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      try { const value = await evaluate(expression); if (value) return value; } catch { /* the page is reloading */ }
      await sleep(1000);
    }
    return null;
  };
  return { evaluate, waitFor, logs, close: () => { try { socket.close(); } catch { /* already closed */ } } };
}
