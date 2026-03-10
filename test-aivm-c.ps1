$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '.')).Path
Set-Location $root
& ./scripts/test-aivm-c.ps1 @args
exit $LASTEXITCODE
