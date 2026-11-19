# Install Hark for Claude Code running natively on Windows (PowerShell, cmd or VS Code without WSL).
# Claude Code in WSL? Use ./install.sh in WSL instead.
#   powershell -ExecutionPolicy Bypass -File install.ps1                 latest GitHub release
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Zip <file>     a local Hark-win-x64.zip
param([string]$Zip = '')
$ErrorActionPreference = 'Stop'

# The release of the repo this folder was cloned from.
$repo = 'Kishara98/Hark'
try {
    $origin = (& git -C $PSScriptRoot remote get-url origin 2>$null)
    if ($origin -match 'github\.com[:/](.+?)(\.git)?$') { $repo = $Matches[1] }
} catch { }

$stage = Join-Path $env:TEMP 'HarkInstall'
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $stage
New-Item -ItemType Directory -Force -Path $stage | Out-Null
if (-not $Zip) {
    $Zip = Join-Path $stage 'Hark-win-x64.zip'
    $url = "https://github.com/$repo/releases/latest/download/Hark-win-x64.zip"
    "Downloading $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $Zip
}
$app = Join-Path $stage 'app'
Unblock-File -Path $Zip
Expand-Archive -Path $Zip -DestinationPath $app -Force

& (Join-Path $PSScriptRoot 'install\install.ps1') -SourceDir $app
& (Join-Path $env:LOCALAPPDATA 'Programs\Hark\HarkHook.exe') install
if ($LASTEXITCODE -ne 0) { Write-Error 'Could not add the Claude Code hooks.' }
''
'Hark is running (see the tray icon). Start a new Claude Code session, or reload VS Code, so it loads the hooks.'
