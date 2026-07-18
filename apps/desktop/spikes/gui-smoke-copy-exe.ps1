$ErrorActionPreference = 'Stop'
. c:\repo\binexus-platform\apps\desktop\spikes\gui-smoke-helpers.ps1
Get-Process binexus-desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
Copy-Item 'c:\repo\binexus-platform\apps\desktop\src-tauri\target\release\binexus-desktop.exe' (Get-BinexusExe) -Force
Write-Host "COPIED_EXE"
# E: start from clean paired — use existing config if paired; else skip
# Ensure WCM+config for paired: run app, but for E we need Paired config + delete WCM
# Re-create paired via previous profile if still present
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9222 --remote-allow-origins=*'
$p = Start-Process -FilePath (Get-BinexusExe) -PassThru
Start-Sleep 5
Write-Host "PID=$($p.Id)"
