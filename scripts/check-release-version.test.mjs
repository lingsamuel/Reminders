import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkReleaseVersion } from './check-release-version.mjs';

const repository = fileURLToPath(new URL('../', import.meta.url));
const currentVersion = JSON.parse(readFileSync(join(repository, 'src/Reminders.Desktop/package.json'))).version;
const files = ['Directory.Build.props', 'src/Reminders.Windows/app.manifest',
  'src/Reminders.Desktop/package.json', 'src/Reminders.Desktop/package-lock.json',
  'src/Reminders.Desktop/src-tauri/tauri.conf.json', 'src/Reminders.Desktop/src-tauri/Cargo.toml',
  'src/Reminders.Desktop/src-tauri/Cargo.lock'];

function fixture(run) {
  const root = mkdtempSync(join(tmpdir(), 'reminders-release-'));
  try {
    for (const file of files) {
      mkdirSync(dirname(join(root, file)), { recursive: true });
      const content = readFileSync(join(repository, file), 'utf8');
      writeFileSync(join(root, file), file.endsWith('app.manifest')
        ? content.replace(`${currentVersion.split('-')[0]}.0`, '1.2.3.0')
        : content.replaceAll(currentVersion, '1.2.3'));
    }
    return run(root);
  } finally { rmSync(root, { recursive: true, force: true }); }
}

test('accepts current branch and matching release tag', () => {
  assert.equal(checkReleaseVersion(repository), currentVersion);
  assert.equal(checkReleaseVersion(repository, `v${currentVersion}`), currentVersion);
});

test('rejects tags that do not match the built version', () => {
  assert.throws(() => checkReleaseVersion(repository, 'v99.99.99'), /Tag version/);
});

test('rejects malformed and non-SemVer release tags', () => {
  for (const tag of ['0.1.1', 'v01.1.1', 'v1.2', 'v1.2.3-01', 'v1.2.3-rc..1', 'v1.2.3;echo bad'])
    assert.throws(() => checkReleaseVersion(repository, tag), /Invalid release tag/);
});

for (const file of files) {
  test(`rejects version drift in ${file}`, () => fixture(root => {
    const path = join(root, file);
    writeFileSync(path, readFileSync(path, 'utf8').replaceAll('1.2.3', '9.8.7'));
    assert.throws(() => checkReleaseVersion(root), /Version mismatch/);
  }));
}

test('accepts matching prerelease while retaining numeric Windows manifest version', () => fixture(root => {
  for (const file of files.filter(file => !file.endsWith('app.manifest'))) {
    const path = join(root, file);
    writeFileSync(path, readFileSync(path, 'utf8').replaceAll('1.2.3', '1.2.3-rc.1'));
  }
  assert.equal(checkReleaseVersion(root, 'v1.2.3-rc.1'), '1.2.3-rc.1');
}));
