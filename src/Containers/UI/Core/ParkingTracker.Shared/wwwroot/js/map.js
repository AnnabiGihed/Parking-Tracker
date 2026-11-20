/* Parking Tracker map: the only place that touches Leaflet. It draws what C# sends and reports map events back; it holds no
 * parking rules. Colours per style key stay here (KIND_STYLE), everything else (what to draw, popup text, escaping) comes from C#.
 * Leaflet 1.9.4 ships in ../lib/leaflet, so the map starts offline. See docs/architecture.md section 3.3. */

const ROOT = new URL('../', import.meta.url).href;
const WMS_URL = 'https://ows.parking.brussels/ws/ParkingBrussels/ows';
const TILE_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';

const KIND_STYLE = {
  covered: '#16a34a', // free with your card
  mixed: '#f59e0b', // partly (check the number)
  pay: '#dc2626',
  red: '#7f1d1d', // red zone (card never valid)
  disc: '#2563eb', // blue zone (parking disc)
  free: '#0d9488', // free for everyone
  freenow: '#2dd4bf', // free right now (outside paid hours)
  check: '#9ca3af', // unknown, check signs
  unknown: '#9ca3af',
};

const ICON_PATH = {
  pin: 'M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5a2.5 2.5 0 0 1 0-5 2.5 2.5 0 0 1 0 5z',
  car: 'M18.92 6.01C18.72 5.42 18.16 5 17.5 5h-11c-.66 0-1.21.42-1.42 1.01L3 12v8c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-1h12v1c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-8l-2.08-5.99zM6.5 16c-.83 0-1.5-.67-1.5-1.5S5.67 13 6.5 13s1.5.67 1.5 1.5S7.33 16 6.5 16zm11 0c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zM5 11l1.5-4.5h11L19 11H5z',
  parking: 'M13 3H6v18h4v-6h3c3.31 0 6-2.69 6-6s-2.69-6-6-6zm.2 8H10V7h3.2c1.1 0 2 .9 2 2s-.9 2-2 2z',
};

let L = null;
let map = null;
let callbacks = null;
let zonesWms = null;
let me = null;
let meAccuracy = null;
let pin = null;
let candidate = null;
let car = null;
let target = null;
let sheetObserver = null;
let popupClickHandler = null;
const groups = {};

const svg = (name) => `<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><path d="${ICON_PATH[name]}"/></svg>`;
const divIcon = (cls, html, size, anchor) => L.divIcon({ className: '', html: `<div class="${cls}">${html}</div>`, iconSize: size, iconAnchor: anchor });
/** The label comes from a service: it is set as text on a created element, never parsed as HTML. */
const poiIcon = (label) => {
  const content = document.createElement('div');
  content.className = 'poi-icon';
  content.textContent = label;
  return L.divIcon({ className: '', html: content, iconSize: [22, 22], iconAnchor: [11, 11] });
};

function loadScript(src) {
  return new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = src;
    script.onload = resolve;
    script.onerror = () => reject(new Error(`Could not load ${src}`));
    document.head.appendChild(script);
  });
}

function loadStyle(href) {
  if (document.querySelector(`link[href="${href}"]`)) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = href;
  document.head.appendChild(link);
}

async function ensureLeaflet() {
  if (window.L) { L = window.L; return; }
  loadStyle(`${ROOT}lib/leaflet/leaflet.css`);
  await loadScript(`${ROOT}lib/leaflet/leaflet.js`);
  L = window.L;
}

function report(method, ...args) {
  if (callbacks) callbacks.invokeMethodAsync(method, ...args).catch(() => { /* the component is gone */ });
}

function group(name) {
  if (!groups[name]) groups[name] = L.layerGroup().addTo(map);
  return groups[name];
}

function styleFor(item) {
  if (item.style === 'area') return { color: '#1d4ed8', weight: 3, dashArray: '8 6', fill: false, interactive: false };
  if (item.style === 'work') return { color: '#ea580c', weight: 2, dashArray: '4 3', fillColor: '#fb923c', fillOpacity: 0.25 };
  const color = KIND_STYLE[item.style] || KIND_STYLE.unknown;
  return { color, weight: 1, fillColor: color, fillOpacity: 0.55, interactive: false };
}

function placeMarker(current, point, options) {
  if (current) current.setLatLng(point);
  return current || L.marker(point, options).addTo(map);
}

function drop(marker) {
  if (marker) map.removeLayer(marker);
  return null;
}

/** Creates the map in the element. view = { lat, lon, zoom }; callbacks = DotNetObjectReference. */
export async function init(element, view, callbackTarget) {
  await ensureLeaflet();
  callbacks = callbackTarget;
  map = L.map(element, { zoomControl: false, preferCanvas: true, attributionControl: true }).setView([view.lat, view.lon], view.zoom);
  L.tileLayer(TILE_URL, {
    maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> · parking.brussels',
  }).addTo(map);
  zonesWms = L.tileLayer.wms(WMS_URL, {
    layers: 'ParkingBrussels:regulation_zones', format: 'image/png', transparent: true, opacity: 0.75, maxZoom: 19, attribution: 'zones © parking.brussels',
  });
  map.on('click', (e) => report('OnMapClick', e.latlng.lat, e.latlng.lng));
  map.on('moveend', () => {
    const b = map.getBounds();
    report('OnViewChanged', b.getSouth(), b.getWest(), b.getNorth(), b.getEast(), map.getZoom());
  });
  // One delegated listener for every popup, registered once: a listener per popupopen would fire the action several times.
  popupClickHandler = (event) => {
    const button = event.target.closest('.leaflet-popup [data-act]');
    if (!button) return;
    event.preventDefault();
    report('OnPopupAction', button.dataset.act, button.dataset.id);
  };
  map.getContainer().addEventListener('click', popupClickHandler);
  const b = map.getBounds();
  report('OnViewChanged', b.getSouth(), b.getWest(), b.getNorth(), b.getEast(), map.getZoom());
}

/** Moves the map. zoom null keeps the zoom level. */
export function setView(lat, lon, zoom) {
  if (zoom == null) { map.panTo([lat, lon], { animate: true }); return; }
  map.setView([lat, lon], zoom);
}

/** Moves the map and zooms in to at least minZoom; a map that is already closer keeps its zoom level. */
export function setViewAtLeast(lat, lon, minZoom) {
  map.setView([lat, lon], Math.max(map.getZoom(), minZoom));
}

export function fitBounds(lat1, lon1, lat2, lon2) {
  map.fitBounds(L.latLngBounds([[lat1, lon1], [lat2, lon2]]).pad(0.4));
}

/** The position of the user with its accuracy circle; null removes it. */
export function setMe(lat, lon, accuracy) {
  if (lat == null) { me = drop(me); meAccuracy = drop(meAccuracy); return; }
  me = placeMarker(me, [lat, lon], { icon: divIcon('me-dot', '', [18, 18], [9, 9]), interactive: false });
  if (meAccuracy) meAccuracy.setLatLng([lat, lon]).setRadius(accuracy);
  else meAccuracy = L.circle([lat, lon], { radius: accuracy, color: '#2563eb', weight: 1, fillOpacity: 0.08, interactive: false }).addTo(map);
}

/** The pin the user dropped; draggable; null removes it. */
export function setPin(lat, lon) {
  if (lat == null) { pin = drop(pin); return; }
  if (pin) { pin.setLatLng([lat, lon]); return; }
  pin = L.marker([lat, lon], { draggable: true, icon: divIcon('pin-marker', svg('pin'), [38, 38], [19, 36]) }).addTo(map);
  pin.on('dragend', () => { const p = pin.getLatLng(); report('OnPinMoved', p.lat, p.lng); });
}

/** The place being chosen for Home or Work: a draggable marker of its own, apart from the pin; null removes it. Pans to it. */
export function setCandidate(lat, lon) {
  if (lat == null) { candidate = drop(candidate); return; }
  if (candidate) candidate.setLatLng([lat, lon]);
  else {
    candidate = L.marker([lat, lon], { draggable: true, zIndexOffset: 1000, icon: divIcon('candidate-marker', svg('pin'), [48, 48], [24, 46]) }).addTo(map);
    candidate.on('dragend', () => { const p = candidate.getLatLng(); report('OnCandidateMoved', p.lat, p.lng); });
  }
  map.setView([lat, lon], Math.max(map.getZoom(), 17));
}

/** Tells Leaflet that the size of its element changed, for example when the layout switches to the picker. */
export function invalidateSize() {
  if (map) map.invalidateSize();
}

/** The parked car with its popup (escaped by C#); null removes it. */
export function setCar(lat, lon, popup) {
  car = drop(car);
  if (lat == null) return;
  car = L.marker([lat, lon], { icon: divIcon('car-marker', svg('car'), [36, 36], [18, 18]) }).bindPopup(popup).addTo(map);
}

/** Opens the popup of the parked car, when there is one. */
export function openCarPopup() {
  if (car) car.openPopup();
}

/** The nearest free spot; null removes it. */
export function setTarget(lat, lon) {
  target = drop(target);
  if (lat == null) return;
  target = L.marker([lat, lon], { icon: divIcon('target-marker', svg('parking'), [30, 30], [15, 15]) }).addTo(map);
}

/** Replaces the areas of a layer. items = [{ style, shape, popup }]; shape = polygons of rings of [lat, lon]. */
export function drawSurfaces(layer, items) {
  const g = group(layer);
  g.clearLayers();
  for (const item of items) {
    const polygon = L.polygon(item.shape, styleFor(item)).addTo(g);
    if (item.popup) polygon.bindPopup(item.popup);
  }
}

/** Replaces the markers of a layer. items = [{ lat, lon, style, label, popup }]. */
export function drawPoints(layer, items) {
  const g = group(layer);
  g.clearLayers();
  for (const item of items) {
    if (item.style === 'space') {
      L.circleMarker([item.lat, item.lon], { radius: 2.5, color: '#334155', weight: 1, fillColor: '#fff', fillOpacity: 1, interactive: false }).addTo(g);
      continue;
    }
    const marker = L.marker([item.lat, item.lon], { icon: poiIcon(item.label || '') }).addTo(g);
    if (item.popup) marker.bindPopup(item.popup);
  }
}

/** Shows or hides the official zone colours drawn by the parking.brussels map server. */
export function showZonesWms(on) {
  if (on) zonesWms.addTo(map); else map.removeLayer(zonesWms);
}

export function clear(layer) {
  if (groups[layer]) groups[layer].clearLayers();
}

/** Keeps the --sheet-h CSS variable equal to the height of the bottom sheet, so that the map controls sit above it. */
export function observeSheet(element) {
  const apply = () => document.documentElement.style.setProperty('--sheet-h', `${window.innerWidth >= 720 ? 0 : element.offsetHeight}px`);
  sheetObserver = new ResizeObserver(apply);
  sheetObserver.observe(element);
  apply();
}

/** Removes the map and every reference held on both sides. */
export function dispose() {
  if (sheetObserver) sheetObserver.disconnect();
  sheetObserver = null;
  if (map && popupClickHandler) map.getContainer().removeEventListener('click', popupClickHandler);
  popupClickHandler = null;
  if (map) map.remove();
  map = null;
  callbacks = null;
  zonesWms = me = meAccuracy = pin = candidate = car = target = null;
  for (const name of Object.keys(groups)) delete groups[name];
}
