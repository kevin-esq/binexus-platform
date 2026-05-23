#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Apply, list, or delete GitHub repository rulesets defined under .github/rulesets/.

.DESCRIPTION
  Modern Rulesets (NOT legacy branch protection). Uses `gh api` for transport.

  - `apply`  : creates each ruleset if it does not exist, otherwise updates it in place
              (idempotent). Matches by name.
  - `list`   : prints the rulesets currently configured on the repo.
  - `delete` : removes a single ruleset by name.

.PARAMETER Action
  apply | list | delete

.PARAMETER Repo
  owner/name. Defaults to the `origin` remote of the current git repo.

.PARAMETER Name
  Ruleset name. Required for -Action delete.

.EXAMPLE
  pwsh -File scripts/apply-rulesets.ps1 -Action apply

.EXAMPLE
  pwsh -File scripts/apply-rulesets.ps1 -Action delete -Name main-protection

.NOTES
  Requires GitHub CLI (`gh`) authenticated with admin permissions on the repo.
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('apply', 'list', 'delete')]
  [string] $Action,

  [Parameter()]
  [string] $Repo,

  [Parameter()]
  [string] $Name
)

$ErrorActionPreference = 'Stop'

function Resolve-Repo {
  if ($Repo) { return $Repo }
  $remote = git config --get remote.origin.url 2>$null
  if (-not $remote) {
    throw "No -Repo provided and no 'origin' remote found."
  }
  if ($remote -match 'github\.com[:/](?<owner>[^/]+)/(?<name>[^/.]+)(\.git)?$') {
    return "$($Matches.owner)/$($Matches.name)"
  }
  throw "Could not parse repo from remote URL '$remote'."
}

function Require-Gh {
  $cmd = Get-Command gh -ErrorAction SilentlyContinue
  if (-not $cmd) {
    throw "GitHub CLI (gh) not found. Install with: winget install --id GitHub.cli"
  }
  $authResult = gh auth status 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "gh is not authenticated. Run: gh auth login"
  }
}

function Get-Rulesets {
  param([string] $TargetRepo)
  $json = gh api "repos/$TargetRepo/rulesets" --jq '.' 2>&1
  if ($LASTEXITCODE -ne 0) {
    if (($json -join "`n") -match 'Upgrade to GitHub Pro|make this repository public') {
      throw @"
GitHub Rulesets are not available for this private repository on the current GitHub plan.

Options:
  1. Make the repository public, then re-run:
     pwsh -File scripts/apply-rulesets.ps1 -Action apply

  2. Upgrade the account/org to GitHub Pro/Team, then re-run the same command.

  3. Use legacy branch protection as a fallback. This repo intentionally keeps modern
     Rulesets in .github/rulesets/ as the canonical policy, but GitHub blocks applying
     them on private repos without Pro/Team.

Original gh output:
$json
"@
    }
    throw "Failed to list rulesets: $json"
  }
  return $json | ConvertFrom-Json
}

function Find-RulesetIdByName {
  param(
    [string] $TargetRepo,
    [string] $RulesetName
  )
  $existing = Get-Rulesets -TargetRepo $TargetRepo
  foreach ($r in $existing) {
    if ($r.name -eq $RulesetName) { return $r.id }
  }
  return $null
}

function Apply-Ruleset {
  param(
    [string] $TargetRepo,
    [string] $FilePath
  )
  if (-not (Test-Path $FilePath)) {
    throw "Ruleset file not found: $FilePath"
  }
  $payload = Get-Content -Raw -Path $FilePath
  $parsed = $payload | ConvertFrom-Json
  $rulesetName = $parsed.name
  if (-not $rulesetName) {
    throw "Ruleset file '$FilePath' is missing a 'name' property."
  }

  $existingId = Find-RulesetIdByName -TargetRepo $TargetRepo -RulesetName $rulesetName
  $tmp = New-TemporaryFile
  try {
    $payload | Set-Content -Path $tmp.FullName -Encoding utf8 -NoNewline
    if ($null -ne $existingId) {
      Write-Host "Updating ruleset '$rulesetName' (id=$existingId)..." -ForegroundColor Cyan
      $output = gh api -X PUT "repos/$TargetRepo/rulesets/$existingId" `
        -H "Accept: application/vnd.github+json" `
        -H "X-GitHub-Api-Version: 2022-11-28" `
        --input $tmp.FullName 2>&1
    }
    else {
      Write-Host "Creating ruleset '$rulesetName'..." -ForegroundColor Cyan
      $output = gh api -X POST "repos/$TargetRepo/rulesets" `
        -H "Accept: application/vnd.github+json" `
        -H "X-GitHub-Api-Version: 2022-11-28" `
        --input $tmp.FullName 2>&1
    }
    if ($LASTEXITCODE -ne 0) {
      $message = ($output | Out-String).Trim()
      Write-Host "  FAILED" -ForegroundColor Red
      Write-Host "  GitHub response:" -ForegroundColor Red
      foreach ($line in $message -split "`r?`n") {
        Write-Host "    $line" -ForegroundColor Red
      }
      $script:RulesetFailures += [pscustomobject]@{
        Name    = $rulesetName
        File    = $FilePath
        Message = $message
      }
      return
    }
    Write-Host "  OK" -ForegroundColor Green
  }
  finally {
    Remove-Item -Force $tmp.FullName -ErrorAction SilentlyContinue
  }
}

function Delete-Ruleset {
  param(
    [string] $TargetRepo,
    [string] $RulesetName
  )
  $existingId = Find-RulesetIdByName -TargetRepo $TargetRepo -RulesetName $RulesetName
  if ($null -eq $existingId) {
    Write-Host "Ruleset '$RulesetName' does not exist. Nothing to delete." -ForegroundColor Yellow
    return
  }
  Write-Host "Deleting ruleset '$RulesetName' (id=$existingId)..." -ForegroundColor Cyan
  gh api -X DELETE "repos/$TargetRepo/rulesets/$existingId" `
    -H "Accept: application/vnd.github+json" `
    -H "X-GitHub-Api-Version: 2022-11-28" | Out-Null
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to delete ruleset '$RulesetName'."
  }
  Write-Host "  OK" -ForegroundColor Green
}

# --- main ---

Require-Gh
$targetRepo = Resolve-Repo
Write-Host "Repo: $targetRepo" -ForegroundColor Magenta

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rulesetsDir = Resolve-Path (Join-Path $scriptDir '..\.github\rulesets')

switch ($Action) {
  'apply' {
    $files = Get-ChildItem -Path $rulesetsDir -Filter '*.json' -File
    if ($files.Count -eq 0) {
      throw "No *.json files in $rulesetsDir"
    }
    $script:RulesetFailures = @()
    foreach ($f in $files) {
      Apply-Ruleset -TargetRepo $targetRepo -FilePath $f.FullName
    }
    Write-Host "`nDone. Current rulesets:" -ForegroundColor Magenta
    Get-Rulesets -TargetRepo $targetRepo |
      Select-Object id, name, target, enforcement |
      Format-Table -AutoSize

    if ($script:RulesetFailures.Count -gt 0) {
      Write-Host "`n$($script:RulesetFailures.Count) ruleset(s) failed:" -ForegroundColor Red
      foreach ($f in $script:RulesetFailures) {
        Write-Host "  - $($f.Name) ($($f.File))" -ForegroundColor Red
      }
      exit 1
    }
  }
  'list' {
    $rulesets = Get-Rulesets -TargetRepo $targetRepo
    if (-not $rulesets -or $rulesets.Count -eq 0) {
      Write-Host "No rulesets configured." -ForegroundColor Yellow
    }
    else {
      $rulesets | Select-Object id, name, target, enforcement | Format-Table -AutoSize
    }
  }
  'delete' {
    if (-not $Name) {
      throw "-Name is required for -Action delete."
    }
    Delete-Ruleset -TargetRepo $targetRepo -RulesetName $Name
  }
}
