# Closes running Hark copies: first asks them to quit normally (so the tray icon goes away), then kills any left.
# Usage: powershell -ExecutionPolicy Bypass -File stop-hark.ps1 [-PathLike '*HarkBuild*']
param([string]$PathLike = '*')

$running = @(Get-Process Hark -ErrorAction SilentlyContinue | Where-Object { $_.Path -like $PathLike })
if ($running.Count -eq 0) { exit 0 }
$exe = $running[0].Path
if ($exe) { Start-Process -FilePath $exe -ArgumentList '--quit' -Wait }
foreach ($p in $running) {
    if (-not $p.WaitForExit(5000)) {
        $p.Kill()
        $p.WaitForExit(5000) | Out-Null
        "Hark did not quit in time and was closed"
    }
}
exit 0
