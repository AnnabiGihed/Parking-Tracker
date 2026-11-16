// Local test server: serves the app and forwards /pbapi/* to the parking.brussels API
// (the API sends no CORS headers, so a browser page can't call it directly; the Android app can).
// Usage: node tools/dev-server.mjs [port]
import { createServer } from 'node:http';
import { request } from 'node:https';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const port = +process.argv[2] || 8765;
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.webmanifest': 'application/manifest+json', '.png': 'image/png' };

createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const proxy = [['/pbapi', 'ows.parking.brussels'], ['/bxl', 'www.bruxelles.be'], ['/bmapi', 'api.mobility.brussels']].find(([p]) => url.pathname.startsWith(p + '/'));
  if (proxy) {
    const [prefix, host] = proxy;
    const up = request({ host, path: url.pathname.slice(prefix.length) + url.search, method: req.method, headers: { 'User-Agent': 'Mozilla/5.0' } }, (r) => {
      const keep = ['content-type', 'content-length', 'etag', 'last-modified'];
      const headers = { 'Cache-Control': 'no-store' };
      for (const k of keep) if (r.headers[k]) headers[k] = r.headers[k];
      res.writeHead(r.statusCode, headers);
      r.pipe(res);
    });
    up.on('error', (e) => { res.writeHead(502); res.end(String(e)); });
    up.end();
    return;
  }
  const file = normalize(join(root, url.pathname === '/' ? 'index.html' : decodeURIComponent(url.pathname)));
  if (!file.startsWith(root)) { res.writeHead(403); res.end(); return; }
  try {
    const body = await readFile(file);
    res.writeHead(200, { 'Content-Type': types[extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
    res.end(body);
  } catch { res.writeHead(404); res.end('Not found'); }
}).listen(port, () => console.log(`Parking Tracker dev server on http://localhost:${port}`));
