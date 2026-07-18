$ErrorActionPreference = 'Stop'
Set-Location c:\repo\binexus-platform
Write-Host "HEAD=$(git rev-parse HEAD)"
Write-Host "SHORT=$(git rev-parse --short HEAD)"
Write-Host "LOG=$(git log -1 --oneline)"
Write-Host "VER=$([System.Environment]::OSVersion.VersionString)"
$nsis = 'c:\repo\binexus-platform\apps\desktop\src-tauri\target\release\bundle\nsis\Binexus_0.1.0_x64-setup.exe'
$h = Get-FileHash $nsis -Algorithm SHA256
$i = Get-Item $nsis
Write-Host "INSTALLER=$($i.FullName)"
Write-Host "SIZE=$($i.Length)"
Write-Host "SHA256=$($h.Hash)"
Write-Host "MTIME=$($i.LastWriteTime.ToString('o'))"
