#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

& ./scripts/test-aivm-c.ps1
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

$report = Join-Path $root '.tmp/aivm-parity-dashboard-ci.md'
$bash = Get-Command bash -ErrorAction SilentlyContinue
if (-not $bash) {
  throw 'bash is required to run scripts/aivm-parity-dashboard.sh'
}

$env:AIVM_DOD_RUN_TESTS = '0'
$env:AIVM_DOD_RUN_BENCH = '0'
& bash ./scripts/aivm-parity-dashboard.sh $report
exit $LASTEXITCODE
