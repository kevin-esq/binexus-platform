#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Sync GitHub labels from .github/labels.yml.

.DESCRIPTION
  Idempotent: creates labels that do not exist, updates color/description of
  labels that do. Existing labels not in the file are left alone.

  Requires: gh CLI authenticated with `repo` scope, and the `powershell-yaml` module.
#>

[CmdletBinding()]
param(
  [Parameter()]
  [string] $Repo
)

$ErrorActionPreference = 'Stop'

function Resolve-Repo {
  if ($Repo) { return $Repo }
  $remote = git config --get remote.origin.url 2>$null
  if (-not $remote) { throw "No -Repo and no 'origin' remote." }
  if ($remote -match 'github\.com[:/](?<owner>[^/]+)/(?<name>[^/.]+)(\.git)?$') {
    return "$($Matches.owner)/$($Matches.name)"
  }
  throw "Could not parse repo from '$remote'."
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
  throw "gh CLI not found. winget install --id GitHub.cli"
}
if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
  Write-Host "Installing powershell-yaml module for current user..." -ForegroundColor Yellow
  Install-Module powershell-yaml -Scope CurrentUser -Force -AllowClobber | Out-Null
}
Import-Module powershell-yaml -ErrorAction Stop

$targetRepo = Resolve-Repo
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$labelsFile = Resolve-Path (Join-Path $scriptDir '..\.github\labels.yml')
$labels = (Get-Content -Raw $labelsFile) | ConvertFrom-Yaml

Write-Host "Repo: $targetRepo" -ForegroundColor Magenta
Write-Host "Syncing $($labels.Count) labels..." -ForegroundColor Cyan

$existing = gh label list --repo $targetRepo --limit 200 --json name | ConvertFrom-Json
$existingNames = $existing | ForEach-Object { $_.name }

foreach ($label in $labels) {
  $color = ([string] $label.color).Trim().TrimStart('#')
  if ($color -notmatch '^[0-9a-fA-F]{6}$') {
    Write-Warning "Skipping label '$($label.name)': invalid color '$($label.color)'"
    continue
  }
  $args = @('label')
  if ($existingNames -contains $label.name) {
    $args += @('edit', $label.name)
  }
  else {
    $args += @('create', $label.name)
  }
  $args += @('--repo', $targetRepo, '--color', $color.ToLowerInvariant())
  if ($label.description) {
    $args += @('--description', $label.description)
  }
  & gh @args | Out-Null
  if ($LASTEXITCODE -ne 0) {
    Write-Warning "Failed to sync label '$($label.name)'"
  }
  else {
    Write-Host "  $($label.name)" -ForegroundColor Green
  }
}

Write-Host "Done." -ForegroundColor Magenta
