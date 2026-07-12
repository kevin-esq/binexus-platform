param(
    [string]$OutputPath = "$PSScriptRoot\..\..\artifacts\openapi.json"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\src\Binexus.Api\Binexus.Api.csproj"
$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Host "Starting API to export OpenAPI..."
$job = Start-Job {
    param($proj)
    Set-Location (Split-Path $proj)
    dotnet run --no-build --project $proj --urls "http://127.0.0.1:5099"
} -ArgumentList (Resolve-Path $project)

try {
    Start-Sleep -Seconds 8
    $dir = Split-Path $OutputPath
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    Invoke-WebRequest -Uri "http://127.0.0.1:5099/openapi/v1.json" -OutFile $OutputPath
    Write-Host "OpenAPI written to $OutputPath"
}
finally {
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
}
