#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preferredSource = Join-Path $root 'src/AiVM.Core/native'
$sourceDir = if ($env:AIVM_C_SOURCE_DIR) { $env:AIVM_C_SOURCE_DIR } else { $preferredSource }
$buildDir = if ($env:AIVM_C_BUILD_DIR) { $env:AIVM_C_BUILD_DIR } else { Join-Path $root '.tmp/aivm-c-build-native' }
$parityReport = if ($env:AIVM_PARITY_REPORT) { $env:AIVM_PARITY_REPORT } else { Join-Path $root '.tmp/aivm-dualrun-manifest/report.txt' }
$parityManifest = if ($env:AIVM_PARITY_MANIFEST) { $env:AIVM_PARITY_MANIFEST } else { Join-Path $sourceDir 'tests/parity_commands_ci.txt' }

$cmakeArgs = @('-S', $sourceDir, '-B', $buildDir, '-DAIVM_BUILD_SHARED=OFF')
if ($IsWindows) {
  $cmakeArgs += @('-G', 'Visual Studio 17 2022', '-A', 'x64')
}

& cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($IsWindows) {
  & cmake --build $buildDir --config Debug
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  & ctest --test-dir $buildDir -C Debug --output-on-failure
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  & cl /nologo /std:c17 /W4 /D_CRT_SECURE_NO_WARNINGS /I .\src\AiVM.Core\native\include /Fe:.\tools\airun.exe `
    .\src\AiCLI\native\airun.c `
    .\src\AiVM.Core\native\src\aivm_types.c `
    .\src\AiVM.Core\native\src\aivm_vm.c `
    .\src\AiVM.Core\native\src\aivm_program.c `
    .\src\AiVM.Core\native\src\aivm_syscall.c `
    .\src\AiVM.Core\native\src\aivm_syscall_contracts.c `
    .\src\AiVM.Core\native\src\aivm_parity.c `
    .\src\AiVM.Core\native\src\aivm_runtime.c `
    .\src\AiVM.Core\native\src\aivm_c_api.c
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  if (!(Test-Path .\tools\airun.exe)) { throw 'failed to compile tools/airun.exe' }
  Remove-Item -Force .\airun.obj -ErrorAction SilentlyContinue
  Remove-Item -Force .\aivm_*.obj -ErrorAction SilentlyContinue
} else {
  & cmake --build $buildDir
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  & ctest --test-dir $buildDir --output-on-failure
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$parityDir = Split-Path -Parent $parityReport
if (-not (Test-Path $parityDir)) {
  New-Item -ItemType Directory -Force -Path $parityDir | Out-Null
}

if ($IsWindows) {
  if (Test-Path (Join-Path $root 'tools/airun.exe')) {
    & (Join-Path $root 'scripts/aivm-dualrun-parity-manifest.ps1') -Manifest $parityManifest -Report $parityReport -Shell 'pwsh'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  } else {
    Set-Content -Path $parityReport -Value 'parity manifest skipped: missing ./tools/airun.exe'
  }
} else {
  if (Test-Path (Join-Path $root 'tools/airun')) {
    & (Join-Path $root 'scripts/aivm-dualrun-parity-manifest.sh') $parityManifest $parityReport
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  } else {
    Set-Content -Path $parityReport -Value 'parity manifest skipped: missing ./tools/airun'
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
