$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/scripts/test.ps1" @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
