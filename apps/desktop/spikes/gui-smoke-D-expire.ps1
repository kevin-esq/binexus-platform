# Scenario D — expire before approve; identity kept; temp cleared; new session OK
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\gui-smoke-helpers.ps1"

$info = Get-SmokeHostInfo
$cdpDir = Join-Path $env:TEMP 'binexus-pw'

function Invoke-Cdp([string]$Scenario) {
  $cdpScript = Join-Path $cdpDir 'gui-smoke-cdp.mjs'
  Copy-Item (Join-Path $PSScriptRoot 'gui-smoke-cdp.mjs') $cdpScript -Force
  Push-Location $cdpDir
  try {
    & node $cdpScript $Scenario
    if ($LASTEXITCODE -ne 0) { throw "CDP $Scenario exit $LASTEXITCODE" }
  } finally {
    Pop-Location
  }
  $path = Join-Path $env:TEMP "binexus-gui-smoke-$Scenario.json"
  if (-not (Test-Path $path)) { throw "CDP evidence missing: $path" }
  Get-Content $path -Raw | ConvertFrom-Json
}

function Get-DeviceIdShort {
  $cfgPath = Join-Path $env:APPDATA 'io.binexus.desktop\config.json'
  $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
  $id = $cfg.deviceId
  if (-not $id) { throw 'deviceId missing' }
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($id.ToString()))
  } finally {
    $sha.Dispose()
  }
  $hash = ([System.BitConverter]::ToString($bytes)).Replace('-', '').Substring(0, 12).ToLowerInvariant()
  return @{ idShort = $id.ToString().Substring(0, 8); idHash12 = $hash }
}

$evidence = @{ scenario = 'D'; startedAt = (Get-Date).ToString('o') }

Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 1
Clear-DesktopProfile
try { $null = Get-BinexusExe } catch { Install-BinexusNsis; Clear-DesktopProfile }

$proc = Start-BinexusGui -DebugPort 9222
Start-Sleep 6

$server = Invoke-Cdp 'A-server'
if (-not $server.ok) { throw "D server failed" }

$before = Get-DeviceIdShort
$evidence.deviceBefore = $before

$session = New-PairingSession -HostInfo $info
$env:BINEXUS_SMOKE_PAIRING_PAYLOAD = "$($session.pairingSessionId):$($session.pairingCode)"
$pair = Invoke-Cdp 'A-pair'
if (-not $pair.ok) { throw "D pair failed: $($pair | ConvertTo-Json -Compress)" }

$cfg = Get-Content (Join-Path $env:APPDATA 'io.binexus.desktop\config.json') -Raw | ConvertFrom-Json
$requestId = $cfg.pairingRequestId
if (-not $requestId) { throw 'pairingRequestId missing' }
$evidence.pairingRequestIdShort = $requestId.ToString().Substring(0, 8)

Remove-Item (Join-Path $env:TEMP 'binexus-gui-smoke-expire-request.ack') -Force -ErrorAction SilentlyContinue
Set-Content -Path (Join-Path $env:TEMP 'binexus-gui-smoke-expire-request.txt') -Value $requestId -NoNewline
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline -and -not (Test-Path (Join-Path $env:TEMP 'binexus-gui-smoke-expire-request.ack'))) {
  Start-Sleep -Milliseconds 400
}
if (-not (Test-Path (Join-Path $env:TEMP 'binexus-gui-smoke-expire-request.ack'))) {
  throw 'Smoke host did not ack expire'
}
$evidence.expireAck = ((Get-Content (Join-Path $env:TEMP 'binexus-gui-smoke-expire-request.ack') -Raw) + '').Trim()

# Click Check approval so poller sees Expired
$resume = Invoke-Cdp 'click-resume'
$evidence.resumeClick = [string]$resume.bodyPreview
Start-Sleep 2
$afterExpire = Invoke-Cdp 'probe'
$evidence.uiAfterExpire = [string]$afterExpire.bodyPreview
$evidence.uiShowsExpired = ([string]$afterExpire.bodyPreview -match 'expired|new code|Pair this terminal')

$cfg2 = Get-Content (Join-Path $env:APPDATA 'io.binexus.desktop\config.json') -Raw | ConvertFrom-Json
$evidence.configStatusAfter = [string]$cfg2.status
$evidence.tempCleared = ($null -eq $cfg2.pairingRequestId -or $cfg2.pairingRequestId -eq '')
$after = Get-DeviceIdShort
$evidence.deviceAfter = $after
$evidence.devicePreserved = ($before.idHash12 -eq $after.idHash12)

# New session with same identity
$session2 = New-PairingSession -HostInfo $info
$env:BINEXUS_SMOKE_PAIRING_PAYLOAD = "$($session2.pairingSessionId):$($session2.pairingCode)"
$pair2 = Invoke-Cdp 'A-pair'
$evidence.repairOk = [bool]$pair2.ok
$evidence.repairPreview = [string]$pair2.bodyPreview
$after2 = Get-DeviceIdShort
$evidence.deviceAfterRepair = $after2
$evidence.deviceStillSame = ($before.idHash12 -eq $after2.idHash12)

$evidence.ok = $evidence.uiShowsExpired -and $evidence.tempCleared -and $evidence.devicePreserved -and $evidence.repairOk -and $evidence.deviceStillSame
$evidence.finishedAt = (Get-Date).ToString('o')
$out = Join-Path $env:TEMP 'binexus-gui-smoke-D.json'
($evidence | ConvertTo-Json -Depth 4) | Set-Content $out -Encoding utf8
if (-not $evidence.ok) { throw "D_FAIL see $out" }
Write-Host "D_PASS evidence=$out"
