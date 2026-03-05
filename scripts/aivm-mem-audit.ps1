#!/usr/bin/env pwsh
param(
  [Parameter(Mandatory = $true, Position = 0)]
  [string]$Target,
  [Parameter(Mandatory = $false, Position = 1)]
  [int]$Iterations = 20
)

$ErrorActionPreference = 'Stop'

if ($Iterations -le 0) {
  throw 'iterations must be a positive integer'
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$airun = Join-Path $root 'tools/airun.exe'
if (-not (Test-Path $airun)) {
  throw "missing runtime: $airun"
}

$vmMode = if ($env:AIVM_MEM_AUDIT_VM_MODE) { $env:AIVM_MEM_AUDIT_VM_MODE } else { '--vm=c' }
$report = if ($env:AIVM_MEM_AUDIT_REPORT) { $env:AIVM_MEM_AUDIT_REPORT } else { Join-Path $root '.tmp/aivm-mem-audit.toml' }
$maxGrowthKb = if ($env:AIVM_LEAK_MAX_RSS_GROWTH_KB) { [int]$env:AIVM_LEAK_MAX_RSS_GROWTH_KB } else { 2048 }

$reportDir = Split-Path -Parent $report
if (-not [string]::IsNullOrWhiteSpace($reportDir)) {
  New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
}

Set-Location $root
& $airun debug profile $Target --iterations $Iterations --max-growth-kb $maxGrowthKb --out $report $vmMode
exit $LASTEXITCODE
