# Gate 6/7 compose smoke (Windows) — isolated COMPOSE_PROJECT_NAME + ports.
# Never kills host processes. If a required smoke port is busy → abort with guidance.
# Usage (repo root):
#   $env:Jwt__SigningKey = 'local-build-signing-key-with-more-than-thirty-two-bytes'
#   pwsh -File apps/backend/scripts/gate6-compose-smoke.ps1
param(
    [switch]$KeepRunning,
    [int]$HealthTimeoutSec = 180,
    [string]$SmokeId = $PID.ToString(),
    [int]$ApiSmokePort = 5112,
    [int]$WorkersSmokePort = 5113,
    [int]$MinioSmokePort = 9100,
    [int]$MinioConsoleSmokePort = 9101,
    [int]$PostgresSmokePort = 55432
)

$ErrorActionPreference = "Stop"
# Match gate6-compose-smoke.sh: KEEP_RUNNING=1 leaves the stack up for Playwright.
if ($env:KEEP_RUNNING -eq '1') { $KeepRunning = $true }
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$ComposeFile = Join-Path $Root "infrastructure\compose\docker-compose.yml"
$LogDir = Join-Path $Root "artifacts\gate6-smoke"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

if ($env:API_SMOKE_PORT) { $ApiSmokePort = [int]$env:API_SMOKE_PORT }
if ($env:WORKERS_SMOKE_PORT) { $WorkersSmokePort = [int]$env:WORKERS_SMOKE_PORT }
if ($env:MINIO_SMOKE_PORT) { $MinioSmokePort = [int]$env:MINIO_SMOKE_PORT }
if ($env:MINIO_CONSOLE_SMOKE_PORT) { $MinioConsoleSmokePort = [int]$env:MINIO_CONSOLE_SMOKE_PORT }
if ($env:POSTGRES_SMOKE_PORT) { $PostgresSmokePort = [int]$env:POSTGRES_SMOKE_PORT }
if ($env:SMOKE_ID) { $SmokeId = $env:SMOKE_ID }

$ProjectName = if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "binexus-smoke-$SmokeId" }
$env:COMPOSE_PROJECT_NAME = $ProjectName
$env:BINEXUS_API_HOST_PORT = "$ApiSmokePort"
$env:BINEXUS_WORKERS_HOST_PORT = "$WorkersSmokePort"
$env:BINEXUS_MINIO_HOST_PORT = "$MinioSmokePort"
$env:BINEXUS_MINIO_CONSOLE_HOST_PORT = "$MinioConsoleSmokePort"
$env:BINEXUS_POSTGRES_HOST_PORT = "$PostgresSmokePort"

$ApiUrl = if ($env:SMOKE_API_URL) { $env:SMOKE_API_URL } else { "http://localhost:$ApiSmokePort" }
$WorkersUrl = if ($env:SMOKE_WORKERS_URL) { $env:SMOKE_WORKERS_URL } else { "http://localhost:$WorkersSmokePort" }

function Test-PortBusy([int]$Port) {
    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Assert-PortFree([int]$Port, [string]$EnvName) {
    if (Test-PortBusy $Port) {
        $owners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            ForEach-Object {
                $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
                if ($p) { "PID $($p.Id) $($p.ProcessName)" } else { "PID $($_.OwningProcess)" }
            } | Select-Object -Unique
        Write-Host "SMOKE FAIL: host port $Port ($EnvName) is already in use." -ForegroundColor Red
        Write-Host "  Smoke uses isolated ports and never kills host processes." -ForegroundColor Yellow
        Write-Host "  Occupied by: $($owners -join '; ')" -ForegroundColor Yellow
        Write-Host "  Free the port or set $EnvName to another free port and re-run." -ForegroundColor Yellow
        throw "Port $Port busy"
    }
}

Assert-PortFree $ApiSmokePort "API_SMOKE_PORT"
Assert-PortFree $WorkersSmokePort "WORKERS_SMOKE_PORT"
Assert-PortFree $MinioSmokePort "MINIO_SMOKE_PORT"
Assert-PortFree $PostgresSmokePort "POSTGRES_SMOKE_PORT"

if (-not $env:Jwt__SigningKey) {
    $env:Jwt__SigningKey = "local-build-signing-key-with-more-than-thirty-two-bytes"
    Write-Host "==> Jwt__SigningKey not set; using DEVELOPMENT-ONLY local default"
}

if (-not $env:IdentitySeed__AdminPassword) { $env:IdentitySeed__AdminPassword = "ChangeMe123!" }
$env:Logistics__Storage__Provider = "MinIO"
$env:Logistics__Storage__Endpoint = "http://minio:9000"
$env:Logistics__Storage__InternalEndpoint = "http://minio:9000"
$env:Logistics__Storage__PublicEndpoint = "http://localhost:$MinioSmokePort"
if (-not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = "Development" }
if (-not $env:DOTNET_ENVIRONMENT) { $env:DOTNET_ENVIRONMENT = "Development" }
$env:RUN_MIGRATIONS = "0"

Set-Location $Root

Write-Host "==> COMPOSE_PROJECT_NAME=$ProjectName"
Write-Host "==> ports api=$ApiSmokePort workers=$WorkersSmokePort minio=$MinioSmokePort postgres=$PostgresSmokePort"

Write-Host "==> docker compose up -p $ProjectName (migrate → api/workers; MinIO)" -ForegroundColor Cyan
docker compose -f $ComposeFile -p $ProjectName up -d --build postgres minio minio-bucket-init migrate api workers
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed" }

function Wait-Http([string]$Url, [string]$Label) {
    Write-Host "==> wait for $Url (${HealthTimeoutSec}s) [$Label]" -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) {
                Write-Host "$Label ready"
                return
            }
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    docker compose -f $ComposeFile -p $ProjectName logs --tail=80 api workers migrate
    throw "$Label not ready"
}

try {
    Wait-Http "$ApiUrl/health/ready" "API"
    Wait-Http "$WorkersUrl/health" "Workers"

    $services = @(docker compose -f $ComposeFile -p $ProjectName ps --services)
    if ($services -contains 'redis') {
        throw "Unexpected redis service in default stack: $($services -join ' ')"
    }

    Write-Host "==> re-run migrator (idempotent)" -ForegroundColor Cyan
    docker compose -f $ComposeFile -p $ProjectName run --rm migrate
    if ($LASTEXITCODE -ne 0) { throw "migrator re-run failed" }

    Write-Host "==> SMOKE_REQUIRE=1 (MinIO)" -ForegroundColor Cyan
    $env:SMOKE_REQUIRE = "1"
    $env:SMOKE_EXPECT_MINIO = "1"
    $env:SMOKE_API_URL = $ApiUrl
    $env:NEXT_PUBLIC_API_URL = $ApiUrl
    $smokeLog = Join-Path $LogDir "smoke.log"
    node (Join-Path $Root "apps\web\scripts\smoke-dotnet.mjs") 2>&1 | Tee-Object -FilePath $smokeLog
    if ($LASTEXITCODE -ne 0) { throw "smoke-dotnet.mjs failed" }

    Write-Host "GATE6 COMPOSE SMOKE PASS" -ForegroundColor Green
}
catch {
    docker compose -f $ComposeFile -p $ProjectName ps -a | Out-File (Join-Path $LogDir "compose-ps.txt")
    docker compose -f $ComposeFile -p $ProjectName logs --no-color --tail=200 api | Out-File (Join-Path $LogDir "api.log")
    docker compose -f $ComposeFile -p $ProjectName logs --no-color --tail=200 workers | Out-File (Join-Path $LogDir "workers.log")
    docker compose -f $ComposeFile -p $ProjectName logs --no-color --tail=80 migrate | Out-File (Join-Path $LogDir "migrate.log")
    throw
}
finally {
    if (-not $KeepRunning) {
        Write-Host "==> docker compose down -p $ProjectName (smoke-owned containers only)" -ForegroundColor Cyan
        docker compose -f $ComposeFile -p $ProjectName --profile web --profile seed down --remove-orphans | Out-Null
    }
}
