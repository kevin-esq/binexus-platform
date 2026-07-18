$ErrorActionPreference = 'Stop'
Get-Process binexus-desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9222 --remote-allow-origins=*'
Start-Process 'C:\Users\Maria\AppData\Local\Binexus\binexus-desktop.exe'
Start-Sleep 6
Write-Host READY
