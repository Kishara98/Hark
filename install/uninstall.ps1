# Removes Hark and its shortcuts. Settings and logs are kept.
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'stop-hark.ps1')

$target = Join-Path $env:LOCALAPPDATA 'Programs\Hark'
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
