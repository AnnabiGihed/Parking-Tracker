// Service worker: makes the app installable and usable with a weak connection.
// App files: network first (so updates arrive), cache as fallback.
// Map tiles and Leaflet: cache first (they rarely change).
const VERSION = 'pt-v1';
const APP = ['./', 'index.html', 'style.css', 'app.js', 'sectors.js', 'manifest.webmanifest',
  'icons/icon-192.png', 'icons/icon-512.png'];
const LIBS = ['https://unpkg.com/leaflet@1.9.4/dist/leaflet.css', 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'];
const MAX_TILES = 1500;

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(VERSION).then((c) => c.addAll([...APP, ...LIBS])).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys()
    .then((keys) => Promise.all(keys.filter((k) => k !== VERSION && k !== 'pt-tiles').map((k) => caches.delete(k))))
    .then(() => self.clients.claim()));
});

async function trimTiles(cache) {
  const keys = await cache.keys();
  for (let i = 0; i < keys.length - MAX_TILES; i++) await cache.delete(keys[i]);
}

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET') return; // Overpass/Nominatim POSTs etc. go straight to the network
  const url = new URL(req.url);

  if (url.hostname === 'tile.openstreetmap.org') {
    e.respondWith(caches.open('pt-tiles').then(async (c) => {
      const hit = await c.match(req);
      if (hit) return hit;
      const res = await fetch(req);
      if (res.ok || res.type === 'opaque') { await c.put(req, res.clone()); trimTiles(c); }
      return res;
    }));
    return;
  }
  if (url.hostname === 'unpkg.com') {
    e.respondWith(caches.match(req).then((hit) => hit || fetch(req)));
    return;
  }
  if (url.origin === self.location.origin) {
    e.respondWith(fetch(req).then((res) => {
      if (res.ok) { const copy = res.clone(); caches.open(VERSION).then((c) => c.put(req, copy)); }
      return res;
    }).catch(() => caches.match(req, { ignoreSearch: true }).then((hit) => hit || caches.match('index.html'))));
  }
});
