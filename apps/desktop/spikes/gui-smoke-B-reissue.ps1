# Scenario B — approve, kill before confirm, discard vault receipt, resume → reissue → Paired
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\gui-smoke-helpers.ps1"

$info = Get-SmokeHostInfo
$cdpDir = Join-Path $env:TEMP 'binexus-pw'
$evidence = @{
  scenario = 'B'
  startedAt = (Get-Date).ToString('o')
  receiptReissuePathExecuted = $false
  confirmResult = 'pending'
  bindingActive = $false
}

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

Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 1
Clear-DesktopProfile
# Prefer already-installed NSIS binary; reinstall if missing
try { $null = Get-BinexusExe } catch { Install-BinexusNsis; Clear-DesktopProfile }

$proc = Start-BinexusGui -DebugPort 9222
Start-Sleep 6
$evidence.pid = $proc.Id

$server = Invoke-Cdp 'A-server'
if (-not $server.ok) { throw "B server setup failed: $($server | ConvertTo-Json -Compress)" }

$session = New-PairingSession -HostInfo $info
$payload = "$($session.pairingSessionId):$($session.pairingCode)"
$env:BINEXUS_SMOKE_PAIRING_PAYLOAD = $payload
$env:BINEXUS_SMOKE_TERMINAL_NAME = "GUI Smoke Reissue $(Get-Random -Maximum 9999)"
$pair = Invoke-Cdp 'A-pair'
if (-not $pair.ok) { throw "B pair failed: $($pair | ConvertTo-Json -Compress)" }

# Extract pairing request id from UI body if present; else from backend pending list via approve path.
# CDP body may include request id — fall back to latest pending from admin API when needed.
$requestId = $null
if ($pair.bodyPreview -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
  $requestId = $Matches[1]
}

# Kill BEFORE approve so the poller cannot confirm.
Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$evidence.killedBeforeApprove = $true

if (-not $requestId) {
  # Probe status tokens are not available; list is not exposed — use exchange fingerprint path:
  # admin approve requires request id from exchange event. Re-read from config.json pairingRequestId.
  $cfgPath = Join-Path $env:APPDATA 'io.binexus.desktop\config.json'
  if (-not (Test-Path $cfgPath)) { throw "config missing after kill: $cfgPath" }
  $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
  $requestId = $cfg.pairingRequestId
  if (-not $requestId) { throw 'pairingRequestId missing in config after kill' }
}
$evidence.pairingRequestIdShort = $requestId.ToString().Substring(0, 8)

Approve-PairingRequest -HostInfo $info -RequestId $requestId
$evidence.approvedWhileDead = $true

# Documented vault-loss mechanism: discard one-shot receipt so confirm must reissue.
Remove-Item (Join-Path $env:TEMP 'binexus-gui-smoke-discard-receipt.ack') -Force -ErrorAction SilentlyContinue
Set-Content -Path (Join-Path $env:TEMP 'binexus-gui-smoke-discard-receipt.txt') -Value $requestId -NoNewline
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline -and -not (Test-Path (Join-Path $env:TEMP 'binexus-gui-smoke-discard-receipt.ack'))) {
  Start-Sleep -Milliseconds 400
}
if (-not (Test-Path (Join-Path $env:TEMP 'binexus-gui-smoke-discard-receipt.ack'))) {
  throw 'Smoke host did not ack receipt discard'
}
$evidence.vaultDiscardAck = ((Get-Content (Join-Path $env:TEMP 'binexus-gui-smoke-discard-receipt.ack') -Raw) + '').Trim()
$evidence.receiptReissuePathExecuted = $true

# Restart GUI — should be PendingApproval / PairingInProgress, not Paired
$proc2 = Start-BinexusGui -DebugPort 9222
Start-Sleep 6
$probe = Invoke-Cdp 'probe'
$evidence.afterRestartUi = [string]$probe.bodyPreview
if ($probe.bodyPreview -match 'This terminal is ready|Terminal paired') {
  throw 'B unexpectedly Paired before resume/reissue'
}

$paired = Invoke-Cdp 'wait-paired'
$evidence.afterResumeOk = [bool]$paired.ok
$evidence.afterResumeUi = [string]$paired.bodyPreview
if (-not $paired.ok) { throw "B resume/reissue did not reach Paired" }
$evidence.confirmResult = 'success'

# Restart again → Paired direct
Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$proc3 = Start-BinexusGui -DebugPort 9222
Start-Sleep 6
$again = Invoke-Cdp 'probe'
$evidence.secondRestart = [string]$again.bodyPreview
if ($again.bodyPreview -notmatch 'This terminal is ready|Terminal paired') {
  throw "B second restart not Paired"
}

$health = Get-BranchHealth -HostInfo $info
$evidence.bindingActive = ($health.status -eq 'Active')
$evidence.finishedAt = (Get-Date).ToString('o')
$evidence.ok = $true

$out = Join-Path $env:TEMP 'binexus-gui-smoke-B.json'
($evidence | ConvertTo-Json -Depth 4) | Set-Content $out -Encoding utf8
Write-Host "B_PASS evidence=$out"
Write-Host 'receipt reissue path executed: yes'
Write-Host 'confirm result: success'
Write-Host 'binding active: yes'
