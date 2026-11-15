// Optimises the captures of capture.mjs: out/raw/*.png -> out/final/*.png, as
// 256-colour palette PNGs (the screens are flat colour and text; no dither).
// Prints a table and fails the run when a file is over 200 KB or the set over
// 1.5 MB, so a screen that grew (or a format that stopped paying) is noticed.
import sharp from 'sharp';
import { mkdirSync, readdirSync, statSync } from 'node:fs';

const MAX_FILE = 200 * 1024;
const MAX_SET = 1.5 * 1024 * 1024;
const rawDir = 'out/raw';
const finalDir = 'out/final';
mkdirSync(finalDir, { recursive: true });

const files = readdirSync(rawDir)
  .filter((f) => f.endsWith('.png') && !f.startsWith('_'))
  .sort();
if (files.length !== 16) throw new Error(`expected 16 captures in ${rawDir}, found ${files.length}`);

const rows = [];
for (const file of files) {
  const info = await sharp(`${rawDir}/${file}`)
    .png({ palette: true, colours: 256, dither: 0, compressionLevel: 9, effort: 10 })
    .toFile(`${finalDir}/${file}`);
  rows.push({
    name: file,
    size: `${info.width}x${info.height}`,
    before: statSync(`${rawDir}/${file}`).size,
    after: info.size,
  });
}

const kb = (bytes) => `${(bytes / 1024).toFixed(1)} KB`;
const pad = (text, width) => String(text).padEnd(width);
console.log(`${pad('name', 32)}${pad('pixels', 12)}${pad('before', 12)}after`);
for (const r of rows) console.log(`${pad(r.name, 32)}${pad(r.size, 12)}${pad(kb(r.before), 12)}${kb(r.after)}`);
const total = rows.reduce((sum, r) => sum + r.after, 0);
console.log(`${pad('total', 44)}${pad(kb(rows.reduce((s, r) => s + r.before, 0)), 12)}${kb(total)}`);

const over = rows.filter((r) => r.after > MAX_FILE);
if (over.length > 0) {
  console.error(`over ${kb(MAX_FILE)}: ${over.map((r) => `${r.name} (${kb(r.after)})`).join(', ')}`);
  process.exit(1);
}
if (total > MAX_SET) {
  console.error(`the set is ${kb(total)}, over ${kb(MAX_SET)}`);
  process.exit(1);
}
