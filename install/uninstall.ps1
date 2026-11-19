# Removes Hark and its shortcuts. Settings and logs are kept.
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'stop-hark.ps1')

$target = Join-Path $env:LOCALAPPDATA 'Programs\Hark'

# Hooks for Claude Code on Windows point to HarkHook.exe: remove them before the exe goes (only when they are there).
$claudeDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$settings = Join-Path $claudeDir 'settings.json'
$hook = Join-Path $target 'HarkHook.exe'
if ((Test-Path $hook) -and (Test-Path $settings) -and (Select-String -Path $settings -Pattern 'HarkHook.exe' -SimpleMatch -Quiet)) {
    & $hook uninstall
}
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Hark.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Startup')) 'Hark.lnk')
)
if (Test-Path $target) { Remove-Item -Recurse -Force $target; "Removed $target" }
foreach ($path in $shortcuts) {
    if (Test-Path $path) { Remove-Item -Force $path; "Removed $path" }
}
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $env:LOCALAPPDATA 'Hark\heartbeat')
'Settings and logs were kept.'
exit 0
