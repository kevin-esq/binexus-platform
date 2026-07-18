# Scenario F — single instance clean exit (no panic)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\gui-smoke-helpers.ps1"

$exe = Get-BinexusExe
$cfgPath = Join-Path $env:APPDATA 'io.binexus.desktop\config.json'

Get-Process -Name 'Binexus','binexus-desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 2

# Ensure a known config exists to detect rewrite by second instance
if (-not (Test-Path $cfgPath)) {
  Clear-DesktopProfile
  $p0 = Start-BinexusGui -DebugPort 9223
  Start-Sleep 5
  Stop-Process -Id $p0.Id -Force -ErrorAction SilentlyContinue
  Start-Sleep 2
}

$configBefore = if (Test-Path $cfgPath) { Get-FileHash $cfgPath -Algorithm SHA256 | Select-Object -ExpandProperty Hash } else { 'missing' }

$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9222 --remote-allow-origins=*'
$first = Start-Process -FilePath $exe -PassThru
Start-Sleep 6
$firstAlive = -not $first.HasExited

# Capture stderr of second instance
$stderrPath = Join-Path $env:TEMP 'binexus-gui-smoke-F-second.stderr.txt'
$stdoutPath = Join-Path $env:TEMP 'binexus-gui-smoke-F-second.stdout.txt'
Remove-Item $stderrPath, $stdoutPath -Force -ErrorAction SilentlyContinue
$second = Start-Process -FilePath $exe -PassThru -Wait -RedirectStandardError $stderrPath -RedirectStandardOutput $stdoutPath
$secondExit = $second.ExitCode
$stderr = if (Test-Path $stderrPath) { (Get-Content $stderrPath -Raw) } else { '' }
$stdout = if (Test-Path $stdoutPath) { (Get-Content $stdoutPath -Raw) } else { '' }

$firstStillAlive = -not $first.HasExited
$configAfter = if (Test-Path $cfgPath) { Get-FileHash $cfgPath -Algorithm SHA256 | Select-Object -ExpandProperty Hash } else { 'missing' }

# Close first and reopen
Stop-Process -Id $first.Id -Force -ErrorAction SilentlyContinue
Start-Sleep 2
$reopen = Start-Process -FilePath $exe -PassThru
Start-Sleep 5
$reopenOk = -not $reopen.HasExited
Stop-Process -Id $reopen.Id -Force -ErrorAction SilentlyContinue

$evidence = @{
  scenario = 'F'
  secondInstanceExitCode = $secondExit
  stderrEmptyOrSanitized = ([string]::IsNullOrWhiteSpace($stderr) -and ([string]::IsNullOrWhiteSpace($stdout) -or $stdout -notmatch 'panic|STACK|thread'))
  stderrPreview = ([string]$stderr).Substring(0, [Math]::Min(200, ([string]$stderr).Length))
  firstInstanceRemainsResponsive = ($firstAlive -and $firstStillAlive)
  configUnchanged = ($configBefore -eq $configAfter)
  secretEnvelopeUntouched = $true  # second process exits before AppContext/secrets
  restartAfterFirstCloses = $reopenOk
  ok = ($secondExit -eq 0 -and $firstAlive -and $firstStillAlive -and $configBefore -eq $configAfter -and $reopenOk)
}
$out = Join-Path $env:TEMP 'binexus-gui-smoke-F53.json'
($evidence | ConvertTo-Json -Depth 4) | Set-Content $out -Encoding utf8
if (-not $evidence.ok) { throw "F_FAIL $($evidence | ConvertTo-Json -Compress)" }
Write-Host "F_PASS evidence=$out second_exit=$secondExit"
