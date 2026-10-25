import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, rmSync, utimesSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const script = fileURLToPath(new URL('./prune-build-artifacts.mjs', import.meta.url));
const zip = version => `Reminders-${version}-win-x64.zip`;
const installer = version => `Reminders_${version}_x64-setup.exe`;
const nsis = target => `src/Reminders.Desktop/src-tauri/target/${target}/release/bundle/nsis`;

function fixture(run) {
  const root = mkdtempSync(join(tmpdir(), 'reminders-retention-'));
  const put = (relative, seconds = 1) => {
    const path = join(root, relative);
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, relative);
    const timestamp = Date.UTC(2020, 0, 1) / 1000 + seconds;
    utimesSync(path, timestamp, timestamp);
  };
  const prune = () => {
    const result = spawnSync(process.execPath, [script, root], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
  };
  try { run({ root, put, prune }); }
  finally { rmSync(root, { recursive: true, force: true }); }
}

test('keeps the three most recently built ZIPs rather than the highest versions', () => fixture(({ root, put, prune }) => {
  const versions = ['9.0.0', '1.2.0', '1.3.0-rc.1', '1.3.0', '1.0.0'];
  versions.forEach((version, i) => put(`artifacts/${zip(version)}`, i + 1));
  prune();
  assert.deepEqual(readdirSync(join(root, 'artifacts')).sort(), versions.slice(-3).map(zip).sort());
  prune();
  assert.equal(readdirSync(join(root, 'artifacts')).length, 3);
}));

test('keeps three ZIPs and three installers per Windows target independently', () => fixture(({ root, put, prune }) => {
  for (let i = 1; i <= 5; i++) {
    put(`artifacts/${zip(`1.0.${i}`)}`, i);
    for (const target of ['x86_64-pc-windows-msvc', 'x86_64-pc-windows-gnu'])
      put(`${nsis(target)}/${installer(`2.0.${i}`)}`, i);
  }
  prune();
  assert.equal(readdirSync(join(root, 'artifacts')).length, 3);
  for (const target of ['x86_64-pc-windows-msvc', 'x86_64-pc-windows-gnu'])
    assert.deepEqual(readdirSync(join(root, nsis(target))).sort(), [3, 4, 5].map(i => installer(`2.0.${i}`)));
}));

test('does nothing when output directories are absent or contain at most three builds', () => fixture(({ root, put, prune }) => {
  prune();
  for (let i = 1; i <= 3; i++) put(`artifacts/${zip(`1.0.${i}`)}`, i);
  prune();
  assert.equal(readdirSync(join(root, 'artifacts')).length, 3);
}));

test('preserves unrelated files, extracted packages and build working directories', () => fixture(({ root, put, prune }) => {
  for (let i = 1; i <= 5; i++) put(`artifacts/${zip(`1.0.${i}`)}`, i);
  const preserved = [
    'artifacts/notes.zip', 'artifacts/Reminders-backup-win-x64.zip',
    'artifacts/Reminders-1.0.0-win-x64.zip.bak', 'artifacts/Reminders-1.0.0-win-arm64.zip',
    'artifacts/backend/Reminders.Backend.dll', 'artifacts/win-x64/Reminders.exe',
    'artifacts/Reminders-1.0.1-win-x64/Reminders.exe',
    `${nsis('x86_64-pc-windows-msvc')}/uninstall.exe`,
  ];
  preserved.forEach(path => put(path));
  mkdirSync(join(root, `artifacts/${zip('0.0.0')}`));
  prune();
  for (const path of preserved) assert.equal(readFileSync(join(root, path), 'utf8'), path);
  assert.deepEqual(readdirSync(join(root, `artifacts/${zip('0.0.0')}`)), []);
}));

test('does not follow symbolic links that resemble old packages', () => fixture(({ root, put, prune }) => {
  put('outside.zip');
  mkdirSync(join(root, 'artifacts'));
  symlinkSync(join(root, 'outside.zip'), join(root, `artifacts/${zip('0.0.0')}`));
  for (let i = 1; i <= 4; i++) put(`artifacts/${zip(`1.0.${i}`)}`, i);
  prune();
  assert.equal(readFileSync(join(root, 'outside.zip'), 'utf8'), 'outside.zip');
  assert.equal(readdirSync(join(root, 'artifacts')).length, 4);
}));

test('equal modification times produce a stable selection', () => fixture(({ root, put, prune }) => {
  for (let i = 1; i <= 5; i++) put(`artifacts/${zip(`1.0.${i}`)}`);
  prune();
  const retained = readdirSync(join(root, 'artifacts')).sort();
  assert.equal(retained.length, 3);
  prune();
  assert.deepEqual(readdirSync(join(root, 'artifacts')).sort(), retained);
}));

function publishFixture({ root, put }, failBuild = false) {
  for (let i = 1; i <= 4; i++) put(`artifacts/${zip(`1.0.${i}`)}`, i);
  put('artifacts/backend/Reminders.Backend.dll');
  put('src/Reminders.Desktop/src-tauri/target/x86_64-pc-windows-gnu/release/reminders-desktop.exe');
  put('loader/x64/WebView2Loader.dll');
  put('README.md'); put('docs/architecture.md'); put('docs/verification.md');
  put('src/Reminders.Desktop/package.json');
  writeFileSync(join(root, 'src/Reminders.Desktop/package.json'), JSON.stringify({ version: '1.0.5' }));
  mkdirSync(join(root, 'scripts'));
  for (const name of ['publish-cross.sh', 'prune-build-artifacts.mjs'])
    writeFileSync(join(root, 'scripts', name), readFileSync(new URL(name, import.meta.url)));
  mkdirSync(join(root, 'bin'));
  for (const name of ['x86_64-w64-mingw32-gcc', 'dotnet', 'npm', 'rustup']) {
    // 只替换编译器和依赖工具；运行真实打包脚本、ZIP 压缩和清理程序。
    writeFileSync(join(root, 'bin', name), `#!/usr/bin/env node
const args = process.argv.slice(2);
if (args.includes('metadata')) console.log(JSON.stringify({ packages: [
  { name: 'webview2-com-sys', manifest_path: require('node:path').join(process.env.TEST_ROOT, 'loader/Cargo.toml') }
] }));
if (args.includes('tauri') && process.env.TEST_FAIL_BUILD === '1') process.exit(1);
`, { mode: 0o755 });
  }
  return spawnSync('bash', [join(root, 'scripts/publish-cross.sh')], {
    encoding: 'utf8',
    env: { ...process.env, PATH: `${join(root, 'bin')}${delimiter}${process.env.PATH}`,
      TEST_ROOT: root, TEST_FAIL_BUILD: failBuild ? '1' : '0' },
  });
}

test('successful cross-publish prunes only after creating the new ZIP', () => fixture(context => {
  const result = publishFixture(context);
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(readdirSync(join(context.root, 'artifacts')).filter(name => name.endsWith('.zip')).sort(),
    [3, 4, 5].map(i => zip(`1.0.${i}`)));
}));

test('failed cross-publish preserves every existing ZIP', () => fixture(context => {
  const result = publishFixture(context, true);
  assert.notEqual(result.status, 0);
  assert.deepEqual(readdirSync(join(context.root, 'artifacts')).filter(name => name.endsWith('.zip')).sort(),
    [1, 2, 3, 4].map(i => zip(`1.0.${i}`)));
}));
