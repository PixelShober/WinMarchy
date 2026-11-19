// Versioning from the pull request title (Conventional Commits):
//   feat: …   → minor (1.2.0 → 1.3.0)
//   fix: … / anything else → patch (1.2.0 → 1.2.1)
//   feat!: … or "BREAKING CHANGE" → major (1.2.0 → 2.0.0)
// The first release is always 1.0.0.
//
//   node scripts/next-version.mjs --check "<title>"      exit code 1 if the title does not follow the convention
//   node scripts/next-version.mjs <last-tag> "<title>"   prints the next version (title also from $PR_TITLE)

export const TITLE = /^(feat|fix|docs|chore|ci|build|refactor|perf|test|style|revert)(\([\w\-./ äöüÄÖÜß]+\))?(!)?: \S.*$/u;

export function bump(lastTag, title) {
  const last = (lastTag ?? '').trim().replace(/^v/, '');
  const m = /^(\d+)\.(\d+)\.(\d+)$/.exec(last);
  if (!m) return '1.0.0';
  let [major, minor, patch] = m.slice(1).map(Number);
  const t = TITLE.exec(title ?? '');
  const breaking = (t && t[3] === '!') || /BREAKING CHANGE/.test(title ?? '');
  if (breaking) return `${major + 1}.0.0`;
  if (t && t[1] === 'feat') return `${major}.${minor + 1}.0`;
  return `${major}.${minor}.${patch + 1}`;
}

const isMain = process.argv[1] && import.meta.url.endsWith(process.argv[1].replace(/\\/g, '/').split('/').pop());
if (isMain) {
  const args = process.argv.slice(2);
  if (args[0] === '--check') {
    const title = args[1] ?? process.env.PR_TITLE ?? '';
    if (!TITLE.test(title)) {
      console.error(`PR title "${title}" does not follow the convention.`);
      console.error('Examples: "feat: Spalten-Layout pro Workspace", "fix(tastenkürzel): Win+K auf deutscher Tastatur", "feat!: neue Einstellungsdatei"');
      process.exit(1);
    }
    console.log(`PR title OK → ${bump('1.0.0', title).replace(/^1\.0\.1$/, 'patch').replace(/^1\.1\.0$/, 'minor').replace(/^2\.0\.0$/, 'major')} release`);
  } else {
    console.log(bump(args[0], args[1] ?? process.env.PR_TITLE ?? ''));
  }
}
