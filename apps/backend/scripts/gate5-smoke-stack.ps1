# Gate 5 smoke stack: Postgres (docker) + migrate + Api + Workers + SMOKE_REQUIRE=1 smoke (+ optional e2e).
# Usage (from repo root):
#   pwsh -File apps/backend/scripts/gate5-smoke-stack.ps1
#   pwsh -File apps/backend/scripts/gate5-smoke-stack.ps1 -SkipE2E
#   pwsh -File apps/backend/scripts/gate5-smoke-stack.ps1 -KeepRunning
param(
    [switch]$SkipE2E,
    [switch]$KeepRunning,
    [string]$PostgresContainer = "binexus-gate5-pg",
    [int]$ApiPort = 5102,
    [int]$HealthTimeoutSec = 60
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$LogDir = Join-Path $Root "artifacts\gate5-smoke"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$ApiLog = Join-Path $LogDir "api.log"
$ApiErrLog = Join-Path $LogDir "api.err.log"
$WorkerLog = Join-Path $LogDir "workers.log"
$WorkerErrLog = Join-Path $LogDir "workers.err.log"
$SmokeLog = Join-Path $LogDir "smoke.log"
$E2ELog = Join-Path $LogDir "e2e.log"

$JwtKey = "local-build-signing-key-with-more-than-thirty-two-bytes"
$PgUser = "binexus"
$PgPass = "binexus"
$PgDb = "binexus_gate5"
$PgPort = 55432
$ConnectionString = "Host=localhost;Port=$PgPort;Database=$PgDb;Username=$PgUser;Password=$PgPass"

$apiProc = $null
$workerProc = $null

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Ensure-Postgres {
    Write-Step "Postgres container '$PostgresContainer'"
    $exists = docker ps -a --filter "name=^/${PostgresContainer}$" --format "{{.Names}}" 2>$null
    if (-not $exists) {
        docker run -d --name $PostgresContainer `
            -e POSTGRES_USER=$PgUser `
            -e POSTGRES_PASSWORD=$PgPass `
            -e POSTGRES_DB=$PgDb `
            -p "${PgPort}:5432" `
            postgres:16-alpine | Out-Null
    } else {
        $running = docker ps --filter "name=^/${PostgresContainer}$" --format "{{.Names}}"
        if (-not $running) {
            docker start $PostgresContainer | Out-Null
        }
    }

    $deadline = (Get-Date).AddSeconds(45)
    do {
        docker exec $PostgresContainer pg_isready -U $PgUser -d $PgDb 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw "Postgres not ready in container $PostgresContainer"
}

function Stop-ManagedProcesses {
    foreach ($p in @($workerProc, $apiProc)) {
        if ($null -eq $p) { continue }
        try {
            if (-not $p.HasExited) {
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }
        } catch {}
    }
}

function Start-DotnetProject {
    param(
        [string]$ProjectPath,
        [string]$OutLog,
        [string]$ErrLog,
        [string]$WorkingDirectory
    )

    # launchSettings can wipe inherited env; set vars in the child PowerShell explicitly.
    $child = @"
`$ErrorActionPreference = 'Stop'
`$env:Database__ConnectionString = '$ConnectionString'
`$env:Jwt__SigningKey = '$JwtKey'
`$env:Jwt__Issuer = 'binexus'
`$env:Jwt__Audience = 'binexus-api'
`$env:Jwt__AccessTokenLifetime = '00:15:00'
`$env:Jwt__RefreshTokenLifetime = '7.00:00:00'
`$env:Jwt__ClockSkew = '00:00:30'
`$env:IdentitySeed__AdminPassword = 'ChangeMe123!'
`$env:Logistics__Storage__Provider = 'Local'
`$env:Logistics__Storage__Endpoint = 'http://localhost:$ApiPort'
`$env:Features__LiquidationKillSwitch = 'true'
`$env:Cors__AllowedOrigins__0 = 'http://localhost:3000'
`$env:ASPNETCORE_ENVIRONMENT = 'Development'
`$env:ASPNETCORE_URLS = 'http://localhost:$ApiPort'
`$env:DOTNET_ENVIRONMENT = 'Development'
Set-Location '$WorkingDirectory'
dotnet run --project '$ProjectPath' --no-launch-profile
"@
    $scriptFile = Join-Path $LogDir ("run-" + [Guid]::NewGuid().ToString("N") + ".ps1")
    Set-Content -Path $scriptFile -Value $child -Encoding UTF8
    return Start-Process -FilePath "pwsh" `
        -ArgumentList @("-NoProfile", "-File", $scriptFile) `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $OutLog `
        -RedirectStandardError $ErrLog `
        -PassThru `
        -NoNewWindow
}

try {
    Ensure-Postgres

    Write-Step "EF database update"
    Push-Location $Root
    try {
        $env:Database__ConnectionString = $ConnectionString
        $env:Jwt__SigningKey = $JwtKey
        dotnet ef database update `
            --project (Join-Path $Root "apps\backend\src\Binexus.Platform\Binexus.Platform.csproj") `
            --startup-project (Join-Path $Root "apps\backend\src\Binexus.Api\Binexus.Api.csproj") `
            --connection $ConnectionString
        if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed" }
    } finally {
        Pop-Location
    }

    Write-Step "Start Binexus.Api (log: $ApiLog)"
    $apiProc = Start-DotnetProject `
        -ProjectPath (Join-Path $Root "apps\backend\src\Binexus.Api\Binexus.Api.csproj") `
        -OutLog $ApiLog `
        -ErrLog $ApiErrLog `
        -WorkingDirectory $Root

    Start-Sleep -Seconds 3
    if ($apiProc.HasExited) {
        Write-Step "Api exited early; rebuilding then restarting"
        dotnet build (Join-Path $Root "apps\backend\src\Binexus.Api\Binexus.Api.csproj") -c Debug
        if ($LASTEXITCODE -ne 0) { throw "dotnet build Api failed" }
        $apiProc = Start-DotnetProject `
            -ProjectPath (Join-Path $Root "apps\backend\src\Binexus.Api\Binexus.Api.csproj") `
            -OutLog $ApiLog `
            -ErrLog $ApiErrLog `
            -WorkingDirectory $Root
    }

    Write-Step "Start Binexus.Workers (log: $WorkerLog)"
    $workerProc = Start-DotnetProject `
        -ProjectPath (Join-Path $Root "apps\backend\src\Binexus.Workers\Binexus.Workers.csproj") `
        -OutLog $WorkerLog `
        -ErrLog $WorkerErrLog `
        -WorkingDirectory $Root

    Write-Step "Wait for /health (timeout ${HealthTimeoutSec}s)"
    $ready = $false
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:$ApiPort/health" -UseBasicParsing -TimeoutSec 2
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        Write-Host "---- API log (tail) ----" -ForegroundColor Red
        if (Test-Path $ApiLog) { Get-Content $ApiLog -Tail 40 }
        if (Test-Path $ApiErrLog) { Get-Content $ApiErrLog -Tail 40 }
        throw "API /health not ready within ${HealthTimeoutSec}s"
    }
    Write-Host "API healthy on :$ApiPort"

    Start-Sleep -Seconds 3
    if ($workerProc.HasExited) {
        Write-Host "---- Workers log (tail) ----" -ForegroundColor Red
        Get-Content $WorkerLog -Tail 40 -ErrorAction SilentlyContinue
        Get-Content $WorkerErrLog -Tail 40 -ErrorAction SilentlyContinue
        throw "Binexus.Workers exited before smoke (Jwt/DB env?)"
    }

    Write-Step "SMOKE_REQUIRE=1 smoke-dotnet.mjs"
    $env:SMOKE_REQUIRE = "1"
    $env:SMOKE_API_URL = "http://localhost:$ApiPort"
    $env:NEXT_PUBLIC_API_URL = "http://localhost:$ApiPort"
    Push-Location $Root
    try {
        node (Join-Path $Root "apps\web\scripts\smoke-dotnet.mjs") 2>&1 | Tee-Object -FilePath $SmokeLog
        if ($LASTEXITCODE -ne 0) {
            Write-Host "---- API log (tail) ----" -ForegroundColor Red
            Get-Content $ApiLog -Tail 40 -ErrorAction SilentlyContinue
            Get-Content $ApiErrLog -Tail 40 -ErrorAction SilentlyContinue
            Write-Host "---- Workers log (tail) ----" -ForegroundColor Red
            Get-Content $WorkerLog -Tail 40 -ErrorAction SilentlyContinue
            Get-Content $WorkerErrLog -Tail 40 -ErrorAction SilentlyContinue
            throw "smoke-dotnet.mjs failed (see $SmokeLog)"
        }
    } finally {
        Pop-Location
    }

    if (-not $SkipE2E) {
        Write-Step "Playwright gate5 e2e (requires web deps + chromium)"
        Push-Location $Root
        try {
            $env:PLAYWRIGHT_API_URL = "http://localhost:$ApiPort"
            $env:NEXT_PUBLIC_API_URL = "http://localhost:$ApiPort"
            pnpm --filter @binexus/web exec playwright test --config=playwright.config.ts 2>&1 | Tee-Object -FilePath $E2ELog
            if ($LASTEXITCODE -ne 0) {
                Write-Host "---- e2e log ----" -ForegroundColor Red
                Get-Content $E2ELog -Tail 80 -ErrorAction SilentlyContinue
                throw "Playwright e2e failed (see $E2ELog)"
            }
        } finally {
            Pop-Location
        }
    } else {
        Write-Host "Skipping e2e (-SkipE2E)"
    }

    Write-Host "GATE5 STACK PASS" -ForegroundColor Green
}
catch {
    Write-Host "GATE5 STACK FAIL: $_" -ForegroundColor Red
    Write-Host "Logs under $LogDir"
    exit 1
}
finally {
    if (-not $KeepRunning) {
        Stop-ManagedProcesses
        Write-Host "Stopped Api/Workers (Postgres container '$PostgresContainer' left running)."
    } else {
        Write-Host "KeepRunning: Api pid=$($apiProc.Id) Workers pid=$($workerProc.Id)"
    }
}
