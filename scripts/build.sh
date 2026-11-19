#!/usr/bin/env bash
# Build the app (Release, x64) and mirror it to the Windows staging folder.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
dotnet build Hark.sln -c Release -p:Platform=x64 -nologo -v:q -clp:ErrorsOnly
# A Hark started from the staging folder holds its files open: close it first.
mkdir -p "$APP_STAGING/install" && cp install/stop-hark.ps1 "$APP_STAGING/install/"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$APP_STAGING_WIN\\install\\stop-hark.ps1" -PathLike '*HarkBuild*' | tr -d '\r' || true
for i in 1 2 3 4 5 6; do
  rm -rf "$APP_STAGING/app" 2>/dev/null && break
  sleep 1
done
if [ -d "$APP_STAGING/app" ]; then
  echo "staging folder is still locked: $APP_STAGING_WIN\\app" >&2
  exit 1
fi
mkdir -p "$APP_STAGING/app"
cp -r src/Hark.App/bin/x64/Release/net48/. "$APP_STAGING/app/"
echo "built: $APP_STAGING_WIN\\app"
