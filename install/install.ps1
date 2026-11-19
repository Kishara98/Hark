# Installs Hark for the current user (no admin rights needed) and starts it.
# Usage: powershell -ExecutionPolicy Bypass -File install.ps1 -SourceDir <folder with Hark.exe>
param([Parameter(Mandatory = $true)][string]$SourceDir)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $SourceDir 'Hark.exe'))) {
    Write-Error "Hark.exe not found in $SourceDir"
    exit 1
}

# Hark only shows state, so a running copy can simply be closed
& (Join-Path $PSScriptRoot 'stop-hark.ps1')

$target = Join-Path $env:LOCALAPPDATA 'Programs\Hark'
New-Item -ItemType Directory -Force -Path $target | Out-Null
robocopy $SourceDir $target /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Error "Copy failed (robocopy code $LASTEXITCODE)"
    exit 1
}

$exe = Join-Path $target 'Hark.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Hark.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Startup')) 'Hark.lnk')
)
foreach ($path in $shortcuts) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $target
    $link.IconLocation = "$exe,0"
    $link.Description = 'Hark: shows when Claude Code needs you'
    $link.Save()
}

Start-Process -FilePath $exe -WorkingDirectory $target
"Installed to $target and started"
$shortcuts | ForEach-Object { "Shortcut: $_" }
exit 0
