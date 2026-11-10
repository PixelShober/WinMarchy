// Dependency guard: WinMarchy ships with as few NuGet packages as possible. The exe is the .NET
// runtime, WPF and our own code; GlazeWM, YASB and AutoHotkey are separate programs from their
// vendors. Every package that ships needs an entry in "allowed" with its reason.
//   node scripts/check-deps.mjs            (needs the .NET SDK for "dotnet list package")
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const projects = ['src/WinMarchy.App/WinMarchy.App.csproj', 'src/WinMarchy.Core/WinMarchy.Core.csproj'];
// Added implicitly by the .NET SDK for single-file publishing; build-time only, nothing of it ships.
const sdkBuildTools = ['Microsoft.NET.ILLink.Tasks'];
// Packages that ship, each with why .NET alone does not do: keep this list short.
const allowed = {
  // MIT, fully managed, no dependencies: reads 7-Zip archives, which .NET cannot. Bulk Crap
  // Uninstaller publishes its portable build only as .7z.
  SharpCompress: 'Bulk Crap Uninstaller portable (.7z)',
};
const errors = [];

for (const project of projects) {
  const out = execFileSync('dotnet', ['list', join(root, project), 'package', '--include-transitive', '--format', 'json'], { encoding: 'utf8' });
  const ids = new Set();
  for (const p of JSON.parse(out).projects ?? []) {
    for (const fw of p.frameworks ?? []) {
      for (const pkg of [...(fw.topLevelPackages ?? []), ...(fw.transitivePackages ?? [])]) ids.add(pkg.id);
    }
  }
  console.log(`${project}: ${[...ids].join(', ') || '(no packages)'}`);
  for (const id of ids) if (!sdkBuildTools.includes(id) && !(id in allowed)) errors.push(`${project}: unexpected package ${id}`);
}

if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log(`Only allowed packages (${Object.keys(allowed).join(', ')}). OK.`);
