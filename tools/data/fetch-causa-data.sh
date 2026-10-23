#!/usr/bin/env bash
# Test-only: fetch kemiller2002/causa-data at the frozen tag into .cache/causa-data and verify the three
# release identities (CAUSA-STORAGE-RELEASE-v1.0.0.md, STORAGE-007). Nothing fetched here is ever copied
# into src/ or the production site (EXT-DATA-001); scripts/check-no-demo-data.mjs enforces that.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
dest="${CAUSA_DATA_DIR:-$root/.cache/causa-data}"
tag="causa-storage-v1.0.0"
commit="c947c7e354be80766573a782546c8186336774cc"
if [ ! -d "$dest/.git" ]; then
  git init -q "$dest"
  git -C "$dest" remote add origin https://github.com/kemiller2002/causa-data
fi
if [ "$(git -C "$dest" rev-parse -q --verify HEAD 2>/dev/null || true)" != "$commit" ]; then
  git -C "$dest" fetch -q --depth 1 origin "refs/tags/$tag:refs/tags/$tag"
  git -C "$dest" checkout -q --detach "$commit"
fi
[ "$(git -C "$dest" rev-parse HEAD)" = "$commit" ] || { echo "causa-data: expected $commit" >&2; exit 1; }
check() { echo "$2  $dest/$1" | sha256sum -c --quiet - || { echo "causa-data identity mismatch: $1" >&2; exit 1; }; }
check contract/v1.0.0/schemas/bundle.json 0a68af8c93190d2e4d6c27beb2f8cb67c3aebdda586d01beb7122b71af179152
check fixtures/v1.0.0/manifest.json f650a2e509a420872b46177b21f57ef6ebd7625ae4c69dd92198d3ca472c2b86
check compatibility/v1.0.0/lock.json 6a7e7802ff2a3ff500ab6065662514a96e09e6c15eb30212ba1377fcc5bf8fc5
echo "causa-data $commit ($tag) at $dest; schema, dataset and release identities verified"
