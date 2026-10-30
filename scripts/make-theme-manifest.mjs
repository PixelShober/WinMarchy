// Builds src/WinMarchy.Core/Resources/omarchy-themes.json from a checkout of omacom/omarchy at a
// pinned tag: every palette (colors.toml) is embedded, previews and wallpapers are listed with
// size and SHA-256 so WinMarchy can download and verify them from raw.githubusercontent.com.
//   git clone --depth 1 --branch v4.0.6 https://github.com/omacom/omarchy /tmp/omarchy
//   node scripts/make-theme-manifest.mjs /tmp/omarchy v4.0.6
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const [checkout, tag] = process.argv.slice(2);
if (!checkout || !tag) {
  console.error('usage: node scripts/make-theme-manifest.mjs <omarchy checkout> <tag>');
  process.exit(1);
}
const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const themesDir = join(checkout, 'themes');
const sha = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');
const asset = (file, rel) => ({ file: rel, size: statSync(file).size, sha256: sha(file) });
const title = (name) => name.split('-').map((w) => w[0].toUpperCase() + w.slice(1)).join(' ');

const themes = [];
for (const name of readdirSync(themesDir).sort()) {
  const dir = join(themesDir, name);
  const colorsFile = join(dir, 'colors.toml');
  if (!statSync(dir).isDirectory() || !existsSync(colorsFile)) continue;
  const colors = {};
  let mode = 'dark';
  for (const line of readFileSync(colorsFile, 'utf8').split('\n')) {
    const m = /^\s*([A-Za-z0-9_-]+)\s*=\s*"([^"]*)"/.exec(line);
    if (!m) continue;
    if (m[1] === 'mode') mode = m[2];
    else colors[m[1]] = m[2];
  }
  let vscode = null;
  if (existsSync(join(dir, 'vscode.json'))) {
    const v = JSON.parse(readFileSync(join(dir, 'vscode.json'), 'utf8'));
    vscode = { name: v.name, extension: v.extension };
  }
  const backgroundsDir = join(dir, 'backgrounds');
  const backgrounds = existsSync(backgroundsDir)
    ? readdirSync(backgroundsDir).filter((f) => /\.(png|jpe?g|webp)$/i.test(f)).sort().map((f) => asset(join(backgroundsDir, f), `backgrounds/${f}`))
    : [];
  themes.push({
    name,
    title: title(name),
    mode,
    colors,
    vscode,
    preview: existsSync(join(dir, 'preview.png')) ? asset(join(dir, 'preview.png'), 'preview.png') : null,
    backgrounds,
  });
}

const manifest = {
  source: 'omacom/omarchy',
  tag,
  baseUrl: `https://raw.githubusercontent.com/omacom/omarchy/${tag}/themes/`,
  license: 'MIT, Copyright (c) David Heinemeier Hansson',
  themes,
};
const out = join(root, 'src/WinMarchy.Core/Resources/omarchy-themes.json');
writeFileSync(out, JSON.stringify(manifest, null, 2) + '\n');
console.log(`${themes.length} themes from ${tag} → ${out}`);
