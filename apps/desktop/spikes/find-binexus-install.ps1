$ErrorActionPreference = 'Continue'
Write-Host '--- Programs ---'
@(
  "$env:LOCALAPPDATA\Programs\Binexus",
  "$env:LOCALAPPDATA\Binexus",
  "$env:ProgramFiles\Binexus",
  "${env:ProgramFiles(x86)}\Binexus"
) | ForEach-Object {
  if (Test-Path $_) {
    Write-Host "DIR=$_"
    Get-ChildItem $_ -Recurse -Filter *.exe -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_.FullName }
  }
}
Write-Host '--- Start Menu ---'
Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter '*Binexus*' -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_.FullName }
Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter '*Binexus*' -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_.FullName }
Write-Host '--- Uninstall keys ---'
$paths = @(
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
  'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
  'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
foreach ($p in $paths) {
  Get-ItemProperty $p -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -match 'Binexus' } |
    ForEach-Object { Write-Host ("NAME={0} LOC={1} ICON={2}" -f $_.DisplayName, $_.InstallLocation, $_.DisplayIcon) }
}
Write-Host '--- Running process path ---'
Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | ForEach-Object {
  Write-Host ("PROC={0} PATH={1}" -f $_.Id, $_.Path)
}
