$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$OpenApi = Join-Path $Root "artifacts\openapi\binexus-v1.json"
$SdkOut = Join-Path $Root "packages\sdk\src\generated"
$SdkFile = Join-Path $SdkOut "schema.d.ts"

if (-not (Test-Path $OpenApi)) {
    throw "Missing $OpenApi. Run: dotnet build apps/backend/src\Binexus.Api\Binexus.Api.csproj"
}

Push-Location $Root
try {
    pnpm --filter @binexus/sdk exec openapi-typescript $OpenApi -o $SdkFile
    $generated = Get-Content $SdkFile -Raw
    # Normalize newlines so header detection works on Windows and Linux CI.
    $normalized = $generated -replace "`r`n", "`n"
    if (-not ($normalized.StartsWith("/**`n * GENERATED FILE — DO NOT EDIT"))) {
        $header = @"
/**
 * GENERATED FILE — DO NOT EDIT
 * Source: artifacts/openapi/binexus-v1.json
 * Generator: openapi-typescript (via apps/backend/scripts/generate-sdk.ps1)
 */

"@
        [System.IO.File]::WriteAllText($SdkFile, $header + $normalized)
    }

    # Match lint-staged / committed formatting so CI `git diff` stays green.
    pnpm exec prettier --write $SdkFile | Out-Host
    Write-Host "Generated $SdkFile"
}
finally {
    Pop-Location
}
