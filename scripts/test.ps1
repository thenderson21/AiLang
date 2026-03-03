#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

$airun = if ($env:AIRUN_BIN) { $env:AIRUN_BIN } elseif ($IsWindows) { './tools/airun.exe' } else { './tools/airun' }

if ($IsWindows) {
  if (Test-Path './scripts/bootstrap-golden-publish-fixtures.ps1') {
    & ./scripts/bootstrap-golden-publish-fixtures.ps1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
} else {
  & ./scripts/bootstrap-golden-publish-fixtures.sh
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& $airun run --vm=ast src/compiler/aic.aos test examples/golden
exit $LASTEXITCODE
