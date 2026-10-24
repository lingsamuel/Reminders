import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const semver = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;

export function checkReleaseVersion(root, tag) {
  const text = file => readFileSync(resolve(root, file), 'utf8');
  const json = file => JSON.parse(text(file));
  const extract = (file, pattern) => {
    const value = text(file).match(pattern)?.[1];
    if (!value) throw new Error(`Missing version: ${file}`);
    return value;
  };
  const version = extract('Directory.Build.props', /<Version>\s*([^<\s]+)\s*<\/Version>/);
  const match = version.match(semver);
  if (!match || match[4]?.split('.').some(part => /^0\d+$/.test(part)))
    throw new Error(`Invalid project version: ${version}`);
  if (tag) {
    const tagged = tag.startsWith('v') && tag.slice(1).match(semver);
    if (!tagged || tagged[4]?.split('.').some(part => /^0\d+$/.test(part)))
      throw new Error(`Invalid release tag: ${tag}; expected vX.Y.Z or vX.Y.Z-prerelease`);
    if (tag.slice(1) !== version) throw new Error(`Tag version ${tag} differs from project ${version}`);
  }
  const lock = json('src/Reminders.Desktop/package-lock.json');
  const versions = {
    'package.json': json('src/Reminders.Desktop/package.json').version,
    'package-lock.json': lock.version,
    'package-lock root package': lock.packages?.['']?.version,
    'tauri.conf.json': json('src/Reminders.Desktop/src-tauri/tauri.conf.json').version,
    'Cargo.toml': extract('src/Reminders.Desktop/src-tauri/Cargo.toml', /^version\s*=\s*"([^"]+)"/m),
    'Cargo.lock': extract('src/Reminders.Desktop/src-tauri/Cargo.lock', /name = "reminders-desktop"\s+version = "([^"]+)"/),
  };
  for (const [file, actual] of Object.entries(versions))
    if (actual !== version) throw new Error(`Version mismatch: ${file} is ${actual}, expected ${version}`);
  const manifest = extract('src/Reminders.Windows/app.manifest', /<assemblyIdentity\b[^>]*\bversion="([^"]+)"/);
  const numeric = `${match[1]}.${match[2]}.${match[3]}.0`;
  if (manifest !== numeric) throw new Error(`Version mismatch: app.manifest is ${manifest}, expected ${numeric}`);
  return version;
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  try {
    const root = fileURLToPath(new URL('../', import.meta.url));
    console.log(`Version check passed: ${checkReleaseVersion(root, process.argv[2] || undefined)}`);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
