#!/usr/bin/env bash
set -euo pipefail

# Linux/WSL -> Windows portable ZIP. Never starts the desktop application.
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
command -v x86_64-w64-mingw32-gcc >/dev/null || { echo 'Install a MinGW-w64 cross compiler first.' >&2; exit 1; }
rustup target add x86_64-pc-windows-gnu --toolchain stable
dotnet publish src/Reminders.Windows -c Release -r win-x64 --self-contained true -o artifacts/backend
pushd src/Reminders.Desktop >/dev/null
npm ci
rustup run stable npm run tauri -- build --target x86_64-pc-windows-gnu --no-bundle --ci
loader_path="$(rustup run stable cargo metadata --manifest-path src-tauri/Cargo.toml --format-version 1 --locked --offline --filter-platform x86_64-pc-windows-gnu | python3 -c 'import json,sys; from pathlib import Path; p=next(p for p in json.load(sys.stdin)["packages"] if p["name"]=="webview2-com-sys"); print(Path(p["manifest_path"]).parent / "x64" / "WebView2Loader.dll")')"
popd >/dev/null
mkdir -p artifacts/win-x64/backend
cp src/Reminders.Desktop/src-tauri/target/x86_64-pc-windows-gnu/release/reminders-desktop.exe artifacts/win-x64/Reminders.exe
# GNU uses the dynamic WebView2 loader; MSVC builds use the static loader.
cp "$loader_path" artifacts/win-x64/WebView2Loader.dll
cp -a artifacts/backend/. artifacts/win-x64/backend/
cp README.md artifacts/win-x64/
mkdir -p artifacts/win-x64/docs
cp docs/architecture.md docs/verification.md artifacts/win-x64/docs/
python3 - <<'PY'
from pathlib import Path
import json
import shutil
root = Path.cwd()
version = json.loads((root / 'src/Reminders.Desktop/package.json').read_text())['version']
archive = shutil.make_archive(str(root / f'artifacts/Reminders-{version}-win-x64'), 'zip', root / 'artifacts/win-x64')
print(f'Portable ZIP: {archive}')
PY
node "$repo_root/scripts/prune-build-artifacts.mjs"
