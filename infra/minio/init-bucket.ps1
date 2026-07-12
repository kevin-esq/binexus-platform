param(
    [string]$Endpoint = "http://127.0.0.1:9000",
    [string]$AccessKey = "binexus",
    [string]$SecretKey = "binexus12345",
    [string]$Bucket = "binexus-proofs"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

if (-not (Get-Command mc -ErrorAction SilentlyContinue)) {
    throw "MinIO Client (mc) not found on PATH. Install from https://min.io/docs/minio/linux/reference/minio-mc.html"
}

mc alias set binexuslocal $Endpoint $AccessKey $SecretKey
mc mb --ignore-existing "binexuslocal/$Bucket"
mc anonymous set none "binexuslocal/$Bucket"
mc cors set "binexuslocal/$Bucket" (Join-Path $Root "cors.json")
Write-Host "Bucket $Bucket ready with CORS for localhost:3000"
