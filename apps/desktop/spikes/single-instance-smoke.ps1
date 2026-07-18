$exe = 'c:\repo\binexus-platform\apps\desktop\src-tauri\target\release\binexus-desktop.exe'
$p1 = Start-Process -FilePath $exe -WindowStyle Minimized -PassThru
Start-Sleep -Seconds 5
Write-Host ("first_running=" + (-not $p1.HasExited) + " pid=" + $p1.Id + " exit=" + $p1.ExitCode)
$p2 = Start-Process -FilePath $exe -WindowStyle Minimized -PassThru -Wait
Write-Host ("second_exit=" + $p2.ExitCode)
if (-not $p1.HasExited) {
  Stop-Process -Id $p1.Id -Force
  Write-Host 'first_stopped'
}
Write-Host 'done'
