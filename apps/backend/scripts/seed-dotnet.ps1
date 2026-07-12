# One-shot Identity demo seed against host Postgres (.NET).
param(
    [ValidateSet("Development", "Testing")]
    [string]$Environment = "Development"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:DOTNET_ENVIRONMENT = $Environment
Set-Location (Join-Path $Root "apps\backend")
dotnet run --project src/Binexus.Api/Binexus.Api.csproj --no-launch-profile -- --seed
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
