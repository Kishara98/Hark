#!/usr/bin/env bash
# Build and install Hark on this Windows laptop (current user, no admin), then start it.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
scripts/build.sh
mkdir -p "$APP_STAGING/install"
cp install/install.ps1 install/uninstall.ps1 install/stop-hark.ps1 "$APP_STAGING/install/"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$APP_STAGING_WIN\\install\\install.ps1" -SourceDir "$APP_STAGING_WIN\\app" | tr -d '\r'
if ! grep -q "/Hark/record_event.py" "$HOME/.claude/settings.json" 2>/dev/null; then
  echo "The Claude hooks are not installed yet: python3 -I hooks/install_hooks.py install"
fi
