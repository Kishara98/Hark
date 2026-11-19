# Remove Hark and its Claude Code hooks (Claude Code running natively on Windows). Settings and logs are kept.
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'install\uninstall.ps1')
