#!/usr/bin/env pwsh
param(
  [Parameter(Mandatory = $true)][string]$Manifest,
  [string]$Report,
  [string]$Shell = 'pwsh'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tmpDir = Join-Path $root '.tmp/aivm-dualrun-manifest'
if (-not $Report) { $Report = Join-Path $tmpDir 'report.txt' }

if (-not (Test-Path $Manifest)) {
  Write-Error "missing manifest file: $Manifest"
  exit 2
}

New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
Set-Content -Path $Report -Value ''

$caseCount = 0
$lines = Get-Content -Path $Manifest
foreach ($line in $lines) {
  if ([string]::IsNullOrWhiteSpace($line)) { continue }
  if ($line.TrimStart().StartsWith('#')) { continue }

  $parts = $line -split '\|', 7
  if ($parts.Length -lt 3) {
    Write-Error "invalid manifest row: $line"
    exit 2
  }

  $name = $parts[0]
  $leftCmd = $parts[1]
  $rightCmd = $parts[2]
  $expectedStatus = if ($parts.Length -ge 4 -and $parts[3] -ne '') { [int]$parts[3] } else { 0 }
  $expectedLeft = if ($parts.Length -ge 5 -and $parts[4] -ne '') { [int]$parts[4] } else { $expectedStatus }
  $expectedRight = if ($parts.Length -ge 6 -and $parts[5] -ne '') { [int]$parts[5] } else { $expectedStatus }
  if ($parts.Length -ge 7 -and $parts[6] -ne '') {
    Write-Error "invalid manifest row for case '$name': too many fields"
    exit 2
  }

  $caseCount += 1
  $slug = ($name -replace '[^A-Za-z0-9._-]', '_')
  $leftOut = Join-Path $tmpDir "$slug.left.out"
  $rightOut = Join-Path $tmpDir "$slug.right.out"

  if ($Shell -eq 'pwsh') {
    & pwsh -NoProfile -Command $leftCmd *> $leftOut
    $leftStatus = $LASTEXITCODE
    & pwsh -NoProfile -Command $rightCmd *> $rightOut
    $rightStatus = $LASTEXITCODE
  } else {
    & $Shell -lc $leftCmd *> $leftOut
    $leftStatus = $LASTEXITCODE
    & $Shell -lc $rightCmd *> $rightOut
    $rightStatus = $LASTEXITCODE
  }

  if ($leftStatus -ne $expectedLeft -or $rightStatus -ne $expectedRight) {
    Add-Content -Path $Report -Value "case=$name|status=status_mismatch|left_status=$leftStatus|right_status=$rightStatus|expected_left_status=$expectedLeft|expected_right_status=$expectedRight|left_file=$leftOut|right_file=$rightOut"
    Write-Error "status mismatch for case '$name' (left=$leftStatus right=$rightStatus expected_left=$expectedLeft expected_right=$expectedRight)"
    exit 1
  }

  $compareScript = Join-Path $root 'scripts/aivm-parity-compare.sh'
  & $compareScript $leftOut $rightOut *> $null
  $cmpStatus = $LASTEXITCODE
  if ($cmpStatus -eq 0) {
    Add-Content -Path $Report -Value "case=$name|status=equal|left_status=$leftStatus|right_status=$rightStatus|left_file=$leftOut|right_file=$rightOut"
  } else {
    Add-Content -Path $Report -Value "case=$name|status=diff|left_status=$leftStatus|right_status=$rightStatus|left_file=$leftOut|right_file=$rightOut"
    Write-Error "parity mismatch for case '$name'"
    exit 1
  }
}

if ($caseCount -eq 0) {
  Write-Error "manifest contained no executable cases: $Manifest"
  exit 2
}

Write-Host "parity manifest passed: $caseCount case(s)"
