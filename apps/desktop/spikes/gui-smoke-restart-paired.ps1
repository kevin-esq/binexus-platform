$ErrorActionPreference = 'Stop'
. c:\repo\binexus-platform\apps\desktop\spikes\gui-smoke-helpers.ps1
$info = Get-SmokeHostInfo

# Backend binding check after A
$headers = @{ Authorization = "Bearer $($info.adminJwt)" }
# list devices via admin if endpoint exists - fallback: health only
Write-Host "HEALTH=$((Get-BranchHealth -HostInfo $info) | ConvertTo-Json -Compress)"

# Restart for Paired direct
Get-Process binexus-desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9222 --remote-allow-origins=*'
$exe = Get-BinexusExe
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep 6
Write-Host "RESTART_PID=$($p.Id) EXITED=$($p.HasExited)"
