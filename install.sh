#!/usr/bin/env bash
# Install Hark for the current Windows user and add its Claude Code hooks. Run it in WSL.
#   ./install.sh           download the ready-built app from the latest GitHub release (no .NET needed)
#   ./install.sh --build   build it from source instead (needs the .NET 10 SDK in ~/.dotnet)
set -euo pipefail
cd "$(dirname "$0")"
command -v python3 >/dev/null || { echo "Hark needs python3 in WSL." >&2; exit 1; }
command -v wslpath >/dev/null || { echo "Hark runs from WSL on Windows." >&2; exit 1; }
source scripts/env.sh

if [ "${1:-}" = "--build" ]; then
  scripts/install.sh
else
  # The release of the repo this folder was cloned from (HARK_REPO=owner/name overrides it).
  origin="$(git remote get-url origin 2>/dev/null || true)"
  repo="${HARK_REPO:-$(printf '%s' "$origin" | sed -E 's#^(git@github\.com:|https://github\.com/)##; s#\.git$##')}"
  [ -n "$repo" ] || repo="Kishara98/Hark"
  url="${HARK_ZIP_URL:-https://github.com/$repo/releases/latest/download/Hark-win-x64.zip}"   # HARK_ZIP_URL: another zip, e.g. file:///...
  echo "Downloading $url"
  rm -rf "$APP_STAGING/app"
  mkdir -p "$APP_STAGING/app" "$APP_STAGING/install"
  python3 -I - "$url" "$APP_STAGING/app" <<'PY'
import io, sys, urllib.request, zipfile
with urllib.request.urlopen(sys.argv[1], timeout=120) as response:
    zipfile.ZipFile(io.BytesIO(response.read())).extractall(sys.argv[2])
PY
  cp install/install.ps1 install/uninstall.ps1 install/stop-hark.ps1 "$APP_STAGING/install/"
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$APP_STAGING_WIN\\install\\install.ps1" -SourceDir "$APP_STAGING_WIN\\app" | tr -d '\r'
fi

python3 -I hooks/install_hooks.py install
echo
echo "Hark is running (see the tray icon). Start a new Claude Code session, or reload VS Code, so it loads the hooks."
