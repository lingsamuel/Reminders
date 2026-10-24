import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, delimiter } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const script = fileURLToPath(new URL('./create-release.sh', import.meta.url));
const zip = 'Reminders-1.2.3-win-x64.zip', installer = 'Reminders_1.2.3_x64-setup.exe';

function fixture(initial, run) {
  const root = mkdtempSync(join(tmpdir(), 'reminders-gh-'));
  try {
    const bin = join(root, 'bin'), assets = join(root, 'assets'), state = join(root, 'state.json');
    mkdirSync(bin); mkdirSync(assets);
    writeFileSync(join(assets, zip), 'portable');
    writeFileSync(join(assets, installer), 'installer');
    writeFileSync(state, JSON.stringify({ draft: null, assets: [], calls: [], ...initial }));
    // 只模拟 gh；测试不会访问 GitHub 或使用真实 token。
    writeFileSync(join(bin, 'gh'), `#!/usr/bin/env node
const fs=require('node:fs'), path=require('node:path');
const file=process.env.TEST_STATE, s=JSON.parse(fs.readFileSync(file)), args=process.argv.slice(2);
s.calls.push(args); const save=()=>fs.writeFileSync(file,JSON.stringify(s)); save();
if(args[0]!=='release') process.exit(90);
switch(args[1]) {
case 'view':
  if(s.draft===null) process.exit(1);
  console.log(args.includes('isDraft')?s.draft:s.assets.join('\\n')); break;
case 'create': s.draft=true; s.prerelease=args.includes('--prerelease'); save(); break;
case 'upload':
  if(s.failUpload) process.exit(1);
  for(const a of args.slice(3)) { const name=path.basename(a); if(s.assets.includes(name)) process.exit(2); s.assets.push(name); }
  save(); break;
case 'edit': s.draft=false; s.prerelease=args.includes('--prerelease=true'); save(); break;
default: process.exit(91);
}
`, { mode: 0o755 });
    const execute = (tag = 'v1.2.3') => spawnSync('bash', [script, tag, assets], {
      encoding: 'utf8', env: { ...process.env, PATH: `${bin}${delimiter}${process.env.PATH}`, TEST_STATE: state, GH_REPO: 'test/reminders', GH_TOKEN: 'fake-token' },
    });
    run({ execute, assets, state: () => JSON.parse(readFileSync(state)), root });
  } finally { rmSync(root, { recursive: true, force: true }); }
}

test('publishes only after both Windows assets are uploaded', () => fixture({}, ({ execute, state }) => {
  const result = execute(); assert.equal(result.status, 0, result.stderr);
  const data = state(); assert.equal(data.draft, false); assert.equal(data.prerelease, false);
  assert.deepEqual(data.assets.sort(), [zip, installer].sort());
  assert(data.calls.find(call => call[1] === 'create').includes('--verify-tag'));
  assert.equal(data.calls.at(-1)[1], 'edit');
}));

test('marks a prerelease tag as prerelease', () => fixture({}, ({ execute, state }) => {
  const result = execute('v1.2.3-rc.1'); assert.equal(result.status, 0, result.stderr);
  assert.equal(state().prerelease, true);
}));

test('a published release rerun preserves existing assets', () => fixture({ draft: false, assets: [zip, installer] }, ({ execute, state }) => {
  const result = execute(); assert.equal(result.status, 0, result.stderr);
  assert(state().calls.every(call => call[1] === 'view'));
}));

test('a draft rerun uploads only the missing asset', () => fixture({ draft: true, assets: [zip] }, ({ execute, state }) => {
  const result = execute(); assert.equal(result.status, 0, result.stderr);
  const uploads = state().calls.filter(call => call[1] === 'upload');
  assert.equal(uploads.length, 1); assert(uploads[0].at(-1).endsWith(installer));
  assert.equal(state().draft, false);
}));

test('failed uploads keep the release in draft', () => fixture({ failUpload: true }, ({ execute, state }) => {
  assert.notEqual(execute().status, 0);
  assert.equal(state().draft, true); assert(!state().calls.some(call => call[1] === 'edit'));
}));

test('missing an installer fails before any GitHub mutation', () => fixture({}, ({ execute, state, assets }) => {
  rmSync(join(assets, installer));
  const result = execute(); assert.notEqual(result.status, 0); assert.match(result.stderr, /Missing release assets/);
  assert.equal(state().calls.length, 0);
}));
