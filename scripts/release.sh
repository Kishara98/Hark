#!/usr/bin/env bash
# Build the ready-built app for a GitHub release: dist/Hark-win-x64.zip.
# Upload it to the release with exactly that name; ./install.sh downloads it from releases/latest.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
rm -rf src/Hark.App/bin/x64/Release
# No debug symbols: they would carry this machine's build folder into the exe.
dotnet build Hark.sln -c Release -p:Platform=x64 -p:DebugType=none -p:DebugSymbols=false -nologo -v:q -clp:ErrorsOnly
mkdir -p dist
python3 -I - src/Hark.App/bin/x64/Release/net48 dist/Hark-win-x64.zip <<'PY'
import os, sys, zipfile
src, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(src):
        for name in sorted(files):
            path = os.path.join(root, name)
            z.write(path, os.path.relpath(path, src))
print("wrote", out, os.path.getsize(out) // 1024, "KB")
PY
