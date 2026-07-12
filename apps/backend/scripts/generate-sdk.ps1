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
    if (-not $generated.StartsWith("/**`n * GENERATED FILE — DO NOT EDIT")) {
        $header = @"
/**
 * GENERATED FILE — DO NOT EDIT
 * Source: artifacts/openapi/binexus-v1.json
 * Generator: openapi-typescript (via apps/backend/scripts/generate-sdk.ps1)
 */

"@
        Set-Content -Path $SdkFile -Value ($header + $generated) -NoNewline
    }
    Write-Host "Generated $SdkFile"
}
finally {
    Pop-Location
}
