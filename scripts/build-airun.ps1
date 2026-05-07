#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

Write-Warning 'build-airun.ps1 is deprecated; AiLang no longer builds C launchers locally.'
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'build.ps1') host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
