// Dependency guard: WinMarchy ships without any NuGet package. The exe is the .NET runtime, WPF and
// our own code; GlazeWM, YASB and AutoHotkey are separate programs from their vendors.
//   node scripts/check-deps.mjs            (needs the .NET SDK for "dotnet list package")
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const projects = ['src/WinMarchy.App/WinMarchy.App.csproj', 'src/WinMarchy.Core/WinMarchy.Core.csproj'];
// Added implicitly by the .NET SDK for single-file publishing; build-time only, nothing of it ships.
const sdkBuildTools = ['Microsoft.NET.ILLink.Tasks'];
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
  for (const id of ids) if (!sdkBuildTools.includes(id)) errors.push(`${project}: unexpected package ${id}`);
}

if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log('No runtime packages. OK.');
