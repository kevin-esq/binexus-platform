$ErrorActionPreference = 'Stop'
. c:\repo\binexus-platform\apps\desktop\spikes\gui-smoke-helpers.ps1

# Wait for smoke host
$deadline = (Get-Date).AddMinutes(3)
while ((Get-Date) -lt $deadline) {
  if (Test-Path (Join-Path $env:TEMP 'binexus-gui-smoke-host.json')) { break }
  Start-Sleep -Seconds 2
}
if (-not (Test-Path (Join-Path $env:TEMP 'binexus-gui-smoke-host.json'))) {
  throw 'Smoke host did not become ready'
}
$info = Get-SmokeHostInfo
Write-Host "HOST=$($info.baseUrl) INSTANCE=$($info.branchInstanceIdShort)"
$health = Get-BranchHealth -HostInfo $info
Write-Host "HEALTH_STATUS=$($health.status) INSTANCE=$($health.branchInstanceId.Substring(0,8))"

# Uninstall previous if present (best-effort)
$uninst = @(
  (Join-Path $env:LOCALAPPDATA 'Binexus\uninstall.exe'),
  (Join-Path ${env:ProgramFiles} 'Binexus\uninstall.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($uninst) {
  Write-Host "UNINSTALL=$uninst"
  Start-Process -FilePath $uninst -ArgumentList '/S' -Wait -ErrorAction SilentlyContinue
}

Clear-DesktopProfile
Install-BinexusNsis
$exe = Get-BinexusExe
Write-Host "EXE=$exe"
Clear-DesktopProfile

# Launch with CDP
Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 1
$proc = Start-BinexusGui -DebugPort 9222
Start-Sleep 5
Write-Host "PID=$($proc.Id) HAS_EXITED=$($proc.HasExited)"

# Persist launch meta for CDP scripts
@{
  exe = $exe
  pid = $proc.Id
  cdp = 'http://127.0.0.1:9222'
  startedAt = (Get-Date).ToString('o')
} | ConvertTo-Json | Set-Content (Join-Path $env:TEMP 'binexus-gui-smoke-launch.json')
Write-Host 'LAUNCH_READY'
