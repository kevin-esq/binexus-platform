# Proves Gate 6 policy: smoke never kills an external host process.
# 1) Hold :5102 with a TcpListener.
# 2) Occupy API_SMOKE_PORT → smoke exits non-zero at port check (no docker).
# 3) Assert :5102 listener still alive (same PID).
# Usage (repo root): pwsh -File apps/backend/scripts/gate6-smoke-no-kill-host.test.ps1
$ErrorActionPreference = "Continue"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
Set-Location $Root

$holder = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 5102)
$holder.Start()
$heldPid = $PID
Write-Host "==> Holding :5102 with TcpListener in PID $heldPid"

$busy = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 5112)
$busy.Start()
Write-Host "==> Holding smoke API port :5112 to force abort"

try {
    $env:API_SMOKE_PORT = "5112"
    $env:WORKERS_SMOKE_PORT = "5113"
    $env:MINIO_SMOKE_PORT = "9100"
    $env:MINIO_CONSOLE_SMOKE_PORT = "9101"
    $env:POSTGRES_SMOKE_PORT = "55432"
    $env:SMOKE_ID = "nokill-$heldPid"
    $env:Jwt__SigningKey = "local-build-signing-key-with-more-than-thirty-two-bytes"

    $out = & pwsh -NoProfile -File (Join-Path $Root "backend\scripts\gate6-compose-smoke.ps1") 2>&1 | Out-String
    $code = $LASTEXITCODE
    Write-Host $out

    if ($code -eq 0) {
        throw "Expected smoke to abort (non-zero) when API_SMOKE_PORT is busy"
    }
    if ($out -notmatch "Port 5112 busy|already in use|SMOKE FAIL") {
        throw "Smoke failed but not with busy-port message: $out"
    }
    Write-Host "==> Busy-port abort OK (exit $code)"

    $still = Get-NetTCPConnection -LocalPort 5102 -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -eq $heldPid }
    if (-not $still) {
        throw "FAIL: listener on :5102 was terminated by smoke"
    }

    $script = Get-Content (Join-Path $Root "backend\scripts\gate6-compose-smoke.ps1") -Raw
    if ($script -match "Stop-Process|taskkill") {
        throw "FAIL: smoke script still contains host process termination"
    }

    Write-Host "GATE6 NO-KILL HOST PASS (busy abort + :5102 preserved)" -ForegroundColor Green
    exit 0
}
finally {
    $busy.Stop()
    $holder.Stop()
}
