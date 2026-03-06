#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preferredSource = Join-Path $root 'src/AiVM.Core/native'
$sourceDir = if ($env:AIVM_C_SOURCE_DIR) { $env:AIVM_C_SOURCE_DIR } else { $preferredSource }
$parityReport = if ($env:AIVM_PARITY_REPORT) { $env:AIVM_PARITY_REPORT } else { Join-Path $root '.tmp/aivm-dualrun-manifest/report.txt' }
$presetFile = Join-Path $sourceDir 'CMakePresets.json'
if (-not (Test-Path $presetFile)) {
  throw "missing $presetFile; native test flow requires presets"
}

Push-Location $sourceDir
try {
  $defaultTestPreset = if ($IsWindows) { 'aivm-native-windows-test' } else { 'aivm-native-unix-test' }
  $testPreset = if ($env:AIVM_CTEST_PRESET) { $env:AIVM_CTEST_PRESET } else { $defaultTestPreset }
  if ($IsWindows) {
    & cmake --preset aivm-native-windows --fresh -DAIVM_BUILD_SHARED=OFF
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & cmake --build --preset aivm-native-windows-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & ctest --preset $testPreset
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  } else {
    & cmake --preset aivm-native-unix --fresh -DAIVM_BUILD_SHARED=OFF
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & cmake --build --preset aivm-native-unix-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & ctest --preset $testPreset
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
} finally {
  Pop-Location
}

if ($IsWindows) {
  if (Get-Command cl -ErrorAction SilentlyContinue) {
    & (Join-Path $root 'scripts/build-airun.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (!(Test-Path .\tools\airun.exe)) { throw 'failed to compile tools/airun.exe' }
  } else {
    Write-Host 'Skipping tools/airun.exe build in test-aivm-c.ps1: cl.exe is not on PATH.'
  }
}

$parityDir = Split-Path -Parent $parityReport
if (-not (Test-Path $parityDir)) {
  New-Item -ItemType Directory -Force -Path $parityDir | Out-Null
}

Set-Content -Path $parityReport -Value 'task parity checks owned by ctest integration tests (aivm_test_task_edge_parity, aivm_test_airun_smoke, aivm_test_debug_memory_smoke)'

if ($env:AIVM_MEM_LEAK_GATE -eq '1') {
  if ($IsWindows) {
    Write-Host 'AIVM_MEM_LEAK_GATE is not implemented on Windows yet.'
  } else {
    $leakIterations = if ($env:AIVM_LEAK_CHECK_ITERATIONS) { $env:AIVM_LEAK_CHECK_ITERATIONS } else { '50' }
    $leakTarget = if ($env:AIVM_LEAK_CHECK_TARGET) { $env:AIVM_LEAK_CHECK_TARGET } else { Join-Path $root 'src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos' }
    if (-not $env:AIVM_LEAK_MAX_GROWTH_KB) { $env:AIVM_LEAK_MAX_GROWTH_KB = '2048' }
    & (Join-Path $root 'scripts/aivm-mem-leak-check.sh') $leakTarget $leakIterations | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
}

if ($env:AIVM_PERF_SMOKE -eq '1') {
  if ($IsWindows) {
    Write-Host 'AIVM_PERF_SMOKE is not implemented on Windows yet.'
  } else {
    $runs = if ($env:AIVM_PERF_RUNS) { $env:AIVM_PERF_RUNS } else { '10' }
    & (Join-Path $root 'scripts/aivm-c-perf-smoke.sh') $runs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
}
