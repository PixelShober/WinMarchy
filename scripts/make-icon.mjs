// Renders the logo src/WinMarchy.App/Assets/winmarchy.svg into winmarchy.ico
// (PNG-compressed entries, 16–256 px). Needs Playwright with Chromium:  node scripts/make-icon.mjs
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const svgPath = join(root, 'src/WinMarchy.App/Assets/winmarchy.svg');
const svg = readFileSync(svgPath, 'utf8');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

const browser = await chromium.launch();
const page = await browser.newPage();
const encoded = await page.evaluate(
  async ({ svg, sizes }) => {
    const img = new Image();
    img.src = 'data:image/svg+xml;base64,' + btoa(svg);
    await img.decode();
    return sizes.map((size) => {
      const c = document.createElement('canvas');
      c.width = c.height = size;
      const ctx = c.getContext('2d');
      // The tiles sit on whole pixels at 32 px and its multiples; smoothing only for the sizes in between.
      ctx.imageSmoothingEnabled = size % 32 !== 0;
      ctx.imageSmoothingQuality = 'high';
      ctx.drawImage(img, 0, 0, size, size);
      return c.toDataURL('image/png').split(',')[1];
    });
  },
  { svg, sizes },
);
await browser.close();
const pngs = encoded.map((b64) => Buffer.from(b64, 'base64'));

// ICO: 6-byte header, 16-byte directory entry per image, then the PNG data.
const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(sizes.length, 4);
let offset = 6 + 16 * sizes.length;
const entries = sizes.map((size, i) => {
  const e = Buffer.alloc(16);
  e.writeUInt8(size >= 256 ? 0 : size, 0);
  e.writeUInt8(size >= 256 ? 0 : size, 1);
  e.writeUInt16LE(1, 4);
  e.writeUInt16LE(32, 6);
  e.writeUInt32LE(pngs[i].length, 8);
  e.writeUInt32LE(offset, 12);
  offset += pngs[i].length;
  return e;
});
writeFileSync(join(root, 'src/WinMarchy.App/Assets/winmarchy.ico'), Buffer.concat([header, ...entries, ...pngs]));
writeFileSync(join(root, 'src/WinMarchy.App/Assets/winmarchy-256.png'), pngs[pngs.length - 1]);
console.log('winmarchy.ico written with sizes', sizes.join(', '));
