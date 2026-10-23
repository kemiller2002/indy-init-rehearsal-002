#!/usr/bin/env bash
# Check the toolchain the frozen stack requires and say exactly what to install when something is missing.
set -uo pipefail
fail=0
need_sdk="$(python3 -c 'import json;print(json.load(open("global.json"))["sdk"]["version"])')"
if ! command -v dotnet >/dev/null; then
  echo "MISSING .NET SDK $need_sdk: install with 'curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version $need_sdk' and add ~/.dotnet to PATH"; fail=1
elif ! dotnet --list-sdks | grep -q "^$need_sdk "; then
  echo "WRONG .NET SDK: global.json requires $need_sdk (rollForward disable); found: $(dotnet --list-sdks | tr '\n' ' ')"; fail=1
fi
if ! command -v node >/dev/null; then
  echo "MISSING Node.js 24: install Node 24 LTS (Folio 0.4.0 requires node >=24 <25)"; fail=1
elif [ "$(node -p 'process.versions.node.split(".")[0]')" != "24" ]; then
  echo "WRONG Node.js $(node -v): Folio 0.4.0 requires >=24 <25"; fail=1
fi
command -v python3 >/dev/null || { echo "MISSING python3 (used by validation tools)"; fail=1; }
command -v git >/dev/null || { echo "MISSING git"; fail=1; }
[ "$fail" = 0 ] && echo "doctor: toolchain OK (.NET $need_sdk, Node $(node -v))"
exit "$fail"
