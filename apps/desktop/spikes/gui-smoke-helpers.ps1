# GUI smoke driver helpers for PR5 — orchestration only; secrets never logged.
$ErrorActionPreference = 'Stop'

function Get-SmokeHostInfo {
  $path = Join-Path $env:TEMP 'binexus-gui-smoke-host.json'
  if (-not (Test-Path $path)) { throw "Smoke host info missing: $path" }
  Get-Content $path -Raw | ConvertFrom-Json
}

function Clear-DesktopProfile {
  $candidates = @(
    (Join-Path $env:APPDATA 'io.binexus.desktop'),
    (Join-Path $env:LOCALAPPDATA 'io.binexus.desktop'),
    (Join-Path $env:APPDATA 'com.binexus.desktop'),
    (Join-Path $env:LOCALAPPDATA 'com.binexus.desktop')
  )
  foreach ($dir in $candidates) {
    if (Test-Path $dir) {
      Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
      Write-Host "CLEARED_PROFILE=$dir"
    }
  }
  # Product KeyringSecretStore (WCM) — use dedicated smoke bin when available
  $wcm = 'c:\repo\binexus-platform\apps\desktop\src-tauri\target\debug\wcm_delete.exe'
  if (-not (Test-Path $wcm)) {
    $wcm = 'c:\repo\binexus-platform\apps\desktop\src-tauri\target\release\wcm_delete.exe'
  }
  if (Test-Path $wcm) {
    & $wcm 2>$null | Out-Host
  } else {
    try {
      cmdkey /delete:LegacyGeneric:target=io.binexus.desktop:device-secret-envelope-v1 2>$null | Out-Null
    } catch {}
  }
}

function Install-BinexusNsis {
  param([string]$Installer = 'c:\repo\binexus-platform\apps\desktop\src-tauri\target\release\bundle\nsis\Binexus_0.1.0_x64-setup.exe')
  if (-not (Test-Path $Installer)) { throw "Installer missing: $Installer" }
  Write-Host "INSTALLING=$Installer"
  $p = Start-Process -FilePath $Installer -ArgumentList '/S' -Wait -PassThru
  Write-Host "NSIS_EXIT=$($p.ExitCode)"
  if ($p.ExitCode -ne 0) { throw "NSIS install failed: $($p.ExitCode)" }
}

function Get-BinexusExe {
  $candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Binexus\binexus-desktop.exe'),
    (Join-Path $env:LOCALAPPDATA 'Binexus\Binexus.exe'),
    (Join-Path ${env:ProgramFiles} 'Binexus\binexus-desktop.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Binexus\binexus-desktop.exe')
  )
  foreach ($c in $candidates) {
    if (Test-Path $c) { return $c }
  }
  throw 'Installed Binexus.exe not found (NSIS LocalAppData\Binexus)'
}

function Start-BinexusGui {
  param([int]$DebugPort = 9222)
  $exe = Get-BinexusExe
  $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$DebugPort --remote-allow-origins=*"
  Write-Host "LAUNCH=$exe debugPort=$DebugPort"
  return Start-Process -FilePath $exe -PassThru
}

function New-PairingSession {
  param($HostInfo)
  $headers = @{ Authorization = "Bearer $($HostInfo.adminJwt)" }
  $resp = Invoke-RestMethod -Method Post -Uri "$($HostInfo.baseUrl)/branch/pairing/sessions" -Headers $headers -ContentType 'application/json' -Body '{}'
  return $resp
}

function Approve-PairingRequest {
  param($HostInfo, [string]$RequestId)
  $headers = @{ Authorization = "Bearer $($HostInfo.adminJwt)" }
  Invoke-RestMethod -Method Post -Uri "$($HostInfo.baseUrl)/branch/pairing/requests/$RequestId/approve" -Headers $headers | Out-Null
}

function Reject-PairingRequest {
  param($HostInfo, [string]$RequestId)
  $headers = @{ Authorization = "Bearer $($HostInfo.adminJwt)" }
  Invoke-RestMethod -Method Post -Uri "$($HostInfo.baseUrl)/branch/pairing/requests/$RequestId/reject" -Headers $headers | Out-Null
}

function Get-BranchHealth {
  param($HostInfo)
  Invoke-RestMethod -Uri "$($HostInfo.baseUrl)/health/branch"
}
