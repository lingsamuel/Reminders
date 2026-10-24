#!/usr/bin/env bash
set -euo pipefail

release_tag="${1:?Expected a release tag}"
asset_directory="${2:?Expected an asset directory}"
: "${GH_REPO:?Expected GH_REPO}"

if [[ ! "$release_tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo 'Invalid release tag' >&2
  exit 1
fi
shopt -s nullglob
portable=("$asset_directory"/*.zip)
installers=("$asset_directory"/*.exe)
if (( ${#portable[@]} == 0 || ${#installers[@]} == 0 )); then
  echo 'Missing release assets: both portable ZIP and installer are required' >&2
  exit 1
fi

prerelease=false
if [[ "$release_tag" == *-* ]]; then prerelease=true; fi
if ! draft="$(gh release view "$release_tag" --json isDraft --jq '.isDraft')"; then
  options=(--verify-tag --draft --title "Reminders $release_tag" --generate-notes)
  if [[ "$prerelease" == true ]]; then options+=(--prerelease); fi
  gh release create "$release_tag" "${options[@]}"
  draft=true
fi

# 中断后重跑只补缺失附件，不覆盖已经发布的同名文件或手动编辑的说明。
existing="$(gh release view "$release_tag" --json assets --jq '.assets[].name')"
declare -A uploaded=()
while IFS= read -r name; do
  if [[ -n "$name" ]]; then uploaded["$name"]=1; fi
done <<< "$existing"
for file in "${portable[@]}" "${installers[@]}"; do
  name="${file##*/}"
  if [[ "${uploaded[$name]:-}" != 1 ]]; then gh release upload "$release_tag" "$file"; fi
done

# 附件齐备才公开；失败保留草稿，后续运行可继续完成。
if [[ "$draft" == true ]]; then
  gh release edit "$release_tag" --draft=false --prerelease="$prerelease"
fi
