#!/usr/bin/env bash
# Remove Hark from this Windows laptop. Settings and logs are kept. Claude settings are not touched.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
mkdir -p "$APP_STAGING/install"
cp install/uninstall.ps1 install/stop-hark.ps1 "$APP_STAGING/install/"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$APP_STAGING_WIN\\install\\uninstall.ps1" | tr -d '\r'
echo "Claude hooks: removed by ./uninstall.sh, or python3 -I hooks/install_hooks.py uninstall"
