# Source this file: the .NET SDK on PATH (only needed to build) and the Windows folders Hark uses,
# found for the Windows user running this WSL.
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# %LOCALAPPDATA% of the Windows user (cmd.exe starts in /mnt/c so it does not warn about a WSL folder)
_hark_local="$(cd /mnt/c && cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')"
if [ -z "$_hark_local" ] || [ "$_hark_local" = "%LOCALAPPDATA%" ]; then
  echo "Hark: could not find the Windows LOCALAPPDATA folder. Hark runs from WSL on Windows." >&2
  return 1 2>/dev/null || exit 1
fi

# Windows staging folder for builds and downloads (same folder, two path styles)
export APP_STAGING_WIN="$_hark_local\\Temp\\HarkBuild"
export APP_STAGING="$(wslpath -u "$APP_STAGING_WIN")"

# Hark data folder (inbox, heartbeat, logs)
export HARK_DATA="$(wslpath -u "$_hark_local\\Hark")"
unset _hark_local
