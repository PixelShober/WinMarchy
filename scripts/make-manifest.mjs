// Writes latest.json (read by the updater in WinMarchy) and SHA256SUMS.txt for a release.
//   node scripts/make-manifest.mjs --version 1.3.0 --exe out/WinMarchy.exe --notes <url> --out out
import { createHash } from 'node:crypto';
import { readFileSync, statSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';

const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, cur, i, all) => (cur.startsWith('--') ? [...acc, [cur.slice(2), all[i + 1]]] : acc), []),
);
for (const required of ['version', 'exe', 'out']) {
  if (!args[required]) {
    console.error(`missing --${required}`);
    process.exit(1);
  }
}

const sha = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

// Same shape as UpdateManifest in src/WinMarchy.Core/Updates/Updater.cs (camelCase).
const manifest = {
  version: args.version,
  published: new Date().toISOString(),
  exe: { name: basename(args.exe), sha256: sha(args.exe), size: statSync(args.exe).size },
  notes: args.notes ?? null,
};

writeFileSync(join(args.out, 'latest.json'), JSON.stringify(manifest, null, 2) + '\n');
writeFileSync(join(args.out, 'SHA256SUMS.txt'), `${manifest.exe.sha256}  ${manifest.exe.name}\n`);
console.log(JSON.stringify(manifest, null, 2));
