# Scenario A — happy path with fingerprint visibility + restart Paired
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
  Get-Content (Join-Path $env:TEMP "binexus-gui-smoke-$Scenario.json") -Raw | ConvertFrom-Json
}

$evidence = @{ scenario = 'A'; startedAt = (Get-Date).ToString('o') }

Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 2
Clear-DesktopProfile
try { $null = Get-BinexusExe } catch { Install-BinexusNsis; Clear-DesktopProfile }

$proc = Start-BinexusGui -DebugPort 9222
Start-Sleep 6

$server = Invoke-Cdp 'A-server'
if (-not $server.ok) { throw 'A server failed' }

$session = New-PairingSession -HostInfo $info
$env:BINEXUS_SMOKE_PAIRING_PAYLOAD = "$($session.pairingSessionId):$($session.pairingCode)"
$pair = Invoke-Cdp 'A-pair'
$evidence.fingerprintVisibleBeforeApprove = [bool]$pair.fingerprintVisible
$evidence.fingerprintShort = [string]$pair.fingerprintMatch
$evidence.codeCleared = [bool]$pair.codeCleared
if (-not $pair.ok -or -not $pair.fingerprintVisible) {
  throw "A pair/fingerprint failed: $($pair | ConvertTo-Json -Compress)"
}

# Poll briefly — fingerprint must stay visible
Start-Sleep 3
$mid = Invoke-Cdp 'probe'
$evidence.fingerprintStableDuringPolling = ([string]$mid.bodyPreview -match [regex]::Escape($pair.fingerprintMatch))
if (-not $evidence.fingerprintStableDuringPolling) {
  throw "fingerprint disappeared during polling: $($mid.bodyPreview)"
}

$cfg = Get-Content (Join-Path $env:APPDATA 'io.binexus.desktop\config.json') -Raw | ConvertFrom-Json
$requestId = $cfg.pairingRequestId
Approve-PairingRequest -HostInfo $info -RequestId $requestId

$paired = Invoke-Cdp 'wait-paired'
if (-not $paired.ok) { throw 'A did not reach Paired' }
$evidence.pairedOk = $true

Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Start-BinexusGui -DebugPort 9222 | Out-Null
Start-Sleep 6
$restart = Invoke-Cdp 'probe'
$evidence.restartPaired = ([string]$restart.bodyPreview -match 'This terminal is ready|Terminal paired')
if (-not $evidence.restartPaired) { throw 'A restart not Paired' }

$evidence.ok = $true
$evidence.finishedAt = (Get-Date).ToString('o')
$out = Join-Path $env:TEMP 'binexus-gui-smoke-A53.json'
($evidence | ConvertTo-Json -Depth 4) | Set-Content $out -Encoding utf8
Write-Host "A_PASS evidence=$out"
Write-Host "fingerprint visible before admin approval: yes"
Write-Host "fingerprint stable during polling: yes"
Write-Host "fingerprint matches backend/admin view: yes ($($evidence.fingerprintShort))"
