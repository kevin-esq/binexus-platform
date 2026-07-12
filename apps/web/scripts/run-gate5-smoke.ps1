# Thin wrapper: run Gate 5 smoke stack from apps/web.
param(
    [switch]$SkipE2E,
    [switch]$KeepRunning
)
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
& (Join-Path $Root "backend\scripts\gate5-smoke-stack.ps1") -SkipE2E:$SkipE2E -KeepRunning:$KeepRunning
exit $LASTEXITCODE
