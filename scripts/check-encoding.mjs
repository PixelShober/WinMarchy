// Guards the texts: every text file is valid UTF-8 without mojibake, German and English
// locale files have the same keys and placeholders, every key the code uses exists, and the
// German texts use real umlauts (also in the hotkey profiles).
//   node scripts/check-encoding.mjs
import { execSync } from 'node:child_process';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { extname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const textExt = new Set(['.cs', '.csproj', '.props', '.targets', '.xaml', '.manifest', '.json', '.md', '.js', '.mjs', '.css', '.html',
  '.yml', '.yaml', '.txt', '.xml', '.svg', '.ps1', '.ahk', '.sln', '.editorconfig', '.gitignore', '.gitattributes']);
const skipDirs = new Set(['node_modules', 'dist', 'bin', 'obj', '.git', 'artifacts', 'release', 'TestResults']);
// The bundled Zebar client is third-party code (GPL, see its header); it is checked as UTF-8 only.
const thirdParty = new Set(['src/WinMarchy.Core/Resources/Zebar/zebar.js']);

function listFiles() {
  try {
    const out = execSync('git ls-files -co --exclude-standard', { cwd: root, encoding: 'utf8' }).split('\n').filter(Boolean);
    if (out.length > 0) return out;
  } catch {
    // not a git checkout – walk the tree
  }
  const files = [];
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      if (skipDirs.has(name)) continue;
      const full = join(dir, name);
      if (statSync(full).isDirectory()) walk(full);
      else files.push(relative(root, full));
    }
  };
  walk(root);
  return files;
}

// UTF-8 bytes decoded as Windows-1252 turn "ä" into "A-tilde + currency sign" and so on. Written as escapes.
const mojibake = /Ã[¤¶¼\u0084\u0096\u009C\u009F„–œŸš]|â€[žœ“”™š˜]|Â[ ­·]/;
const decoder = new TextDecoder('utf-8', { fatal: true });
const errors = [];

for (const file of listFiles()) {
  const ext = extname(file) || file.slice(file.lastIndexOf('/') + 1);
  if (!textExt.has(ext) && !textExt.has('.' + ext)) continue;
  let text;
  try {
    text = decoder.decode(readFileSync(join(root, file)));
  } catch {
    errors.push(`${file}: is not valid UTF-8`);
    continue;
  }
  if (thirdParty.has(file)) continue;
  const lines = text.split('\n');
  lines.forEach((line, i) => {
    if (mojibake.test(line)) errors.push(`${file}:${i + 1}: looks like broken umlauts (mojibake): ${line.trim().slice(0, 80)}`);
    if (line.includes(String.fromCharCode(0xfffd))) errors.push(`${file}:${i + 1}: contains the replacement character U+FFFD`);
  });
}

// ---- locale files
const flatten = (obj, prefix = '', into = {}) => {
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === 'object') flatten(v, key, into);
    else into[key] = v;
  }
  return into;
};
const locales = 'src/WinMarchy.Core/Resources/Locales';
const de = flatten(JSON.parse(readFileSync(join(root, locales, 'de.json'), 'utf8')));
const en = flatten(JSON.parse(readFileSync(join(root, locales, 'en.json'), 'utf8')));
const ph = (s) => [...String(s).matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');
for (const k of Object.keys(de)) {
  if (!(k in en)) errors.push(`en.json: missing key ${k}`);
  else if (ph(de[k]) !== ph(en[k])) errors.push(`placeholders differ for ${k}: de {${ph(de[k])}} en {${ph(en[k])}}`);
}
for (const k of Object.keys(en)) if (!(k in de)) errors.push(`de.json: missing key ${k}`);

const translit = /\b(fuer|ueber|Ueber|koennen|muessen|waehlen|loeschen|Loeschen|pruefen|Pruefen|zurueck|Zurueck|oeffnen|Oeffnen|schliessen|Schliessen|Groesse|ausfuehren|geloescht|geprueft|Schluessel|aendern|Aenderung|Kuerzel|Tastenkuerzel|naechste|Naechste|hoeher|Hoehe|groesser|Menue)\b/;
const germanTexts = Object.entries(de).map(([k, v]) => [`de.json ${k}`, v]);
const profiles = 'src/WinMarchy.Core/Resources/Hotkeys';
for (const name of readdirSync(join(root, profiles)).filter((f) => f.endsWith('.json'))) {
  const profile = JSON.parse(readFileSync(join(root, profiles, name), 'utf8'));
  for (const b of profile.bindings ?? []) {
    for (const field of ['label', 'note']) if (b[field]?.de) germanTexts.push([`${name} ${b.keys} ${field}`, b[field].de]);
    if (b.group && !(`hotkeys.groups.${b.group}` in de)) errors.push(`${name} ${b.keys}: unknown group "${b.group}"`);
  }
}
for (const [where, v] of germanTexts) if (translit.test(v)) errors.push(`${where}: use real umlauts/ß instead of "${v.match(translit)[0]}"`);

// ---- keys used by the code (literal keys; "prefix." + value families are checked by the self test)
const codeFiles = listFiles().filter((f) => f.startsWith('src/') && /\.(cs|xaml)$/.test(f));
let used = 0;
for (const file of codeFiles) {
  const text = readFileSync(join(root, file), 'utf8');
  const keys = [
    ...[...text.matchAll(/\bL\.(?:T|TryT)\(\s*"([a-zA-Z0-9.\-]+)"/g)].map((m) => m[1]),
    ...[...text.matchAll(/\{ui:T ([a-zA-Z0-9.\-]+)\}/g)].map((m) => m[1]),
    ...[...text.matchAll(/new WinMarchyException\(\s*(?:\w+,\s*)?"(\w+)"/g)].map((m) => `errors.${m[1]}`),
  ];
  for (const key of keys) {
    if (key.endsWith('.')) continue;
    used++;
    if (!(key in de)) errors.push(`${file}: unknown translation key "${key}"`);
  }
}

if (errors.length) {
  console.error(errors.join('\n'));
  console.error(`\n${errors.length} problem(s) found.`);
  process.exit(1);
}
console.log(`Encoding and translations OK (${Object.keys(de).length} keys, ${used} uses in ${codeFiles.length} files).`);
