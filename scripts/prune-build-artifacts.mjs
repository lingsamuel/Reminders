import { readdirSync, statSync, unlinkSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(process.argv[2] ?? fileURLToPath(new URL('../', import.meta.url)));
const version = String.raw`[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?`;

function pruneDirectory(directory, pattern) {
  let entries;
  try { entries = readdirSync(directory, { withFileTypes: true }); }
  catch (error) {
    if (error.code === 'ENOENT') return;
    throw error;
  }
  // 只删除标准发布包文件；目录和符号链接不属于历史构建包。
  const packages = entries.filter(entry => entry.isFile() && pattern.test(entry.name))
    .map(entry => ({ name: entry.name, modified: statSync(join(directory, entry.name)).mtimeMs }))
    .sort((a, b) => b.modified - a.modified || b.name.localeCompare(a.name));
  for (const item of packages.slice(3)) {
    const path = join(directory, item.name);
    unlinkSync(path);
    console.log(`已清理旧构建产物：${path}`);
  }
}

pruneDirectory(join(root, 'artifacts'), new RegExp(`^Reminders-${version}-win-x64\\.zip$`));
for (const target of ['x86_64-pc-windows-msvc', 'x86_64-pc-windows-gnu']) {
  pruneDirectory(join(root, 'src/Reminders.Desktop/src-tauri/target', target, 'release/bundle/nsis'),
    new RegExp(`^Reminders_${version}_x64-setup\\.exe$`));
}
