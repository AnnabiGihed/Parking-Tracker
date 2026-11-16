// Copies the web app into www/, the folder Capacitor packages into the Android app.
import { cpSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const out = join(root, 'www');
const files = ['index.html', 'style.css', 'app.js', 'sectors.js', 'manifest.webmanifest', 'sw.js', 'icons'];

rmSync(out, { recursive: true, force: true });
mkdirSync(out);
for (const f of files) cpSync(join(root, f), join(out, f), { recursive: true });
console.log(`Copied ${files.length} items to www/`);
