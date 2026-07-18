$ErrorActionPreference = 'Stop'
. c:\repo\binexus-platform\apps\desktop\spikes\gui-smoke-helpers.ps1
Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Clear-DesktopProfile
$exe = Get-BinexusExe
Write-Host "EXE=$exe"
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9222 --remote-allow-origins=*'
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep 6
Write-Host "PID=$($proc.Id) EXITED=$($proc.HasExited) PATH=$($proc.Path)"
# Probe CDP
try {
  $ver = Invoke-RestMethod 'http://127.0.0.1:9222/json/version'
  Write-Host "CDP_OK browser=$($ver.Browser)"
} catch {
  Write-Host "CDP_FAIL $_"
}
@{ exe = $exe; pid = $proc.Id; cdp = 'http://127.0.0.1:9222'; startedAt = (Get-Date).ToString('o') } |
  ConvertTo-Json | Set-Content (Join-Path $env:TEMP 'binexus-gui-smoke-launch.json')
