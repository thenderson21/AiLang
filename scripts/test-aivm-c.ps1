#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preferredSource = Join-Path $root 'src/AiVM.Core/native'
$sourceDir = if ($env:AIVM_C_SOURCE_DIR) { $env:AIVM_C_SOURCE_DIR } else { $preferredSource }
$buildDir = if ($env:AIVM_C_BUILD_DIR) { $env:AIVM_C_BUILD_DIR } else { Join-Path $root '.tmp/aivm-c-build-native' }
$parityReport = if ($env:AIVM_PARITY_REPORT) { $env:AIVM_PARITY_REPORT } else { Join-Path $root '.tmp/aivm-dualrun-manifest/report.txt' }
$parityManifest = if ($env:AIVM_PARITY_MANIFEST) { $env:AIVM_PARITY_MANIFEST } else { Join-Path $sourceDir 'tests/parity_commands_ci.txt' }

$presetFile = Join-Path $sourceDir 'CMakePresets.json'
if (Test-Path $presetFile) {
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
} else {
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
  } else {
    & cmake --build $buildDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & ctest --test-dir $buildDir --output-on-failure
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
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

$runLegacySmoke = ($env:AIVM_LEGACY_SMOKE -eq '1')
if ($runLegacySmoke) {
if ($IsWindows) {
  if (Test-Path (Join-Path $root 'tools/airun.exe')) {
    & (Join-Path $root 'tools/airun.exe') run (Join-Path $root 'src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos') --vm=c | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'native bytecode .aos run smoke failed' }
    $nativePublishDir = Join-Path $root '.tmp/aivm-c-native-publish-smoke'
    if (Test-Path $nativePublishDir) { Remove-Item -Recurse -Force $nativePublishDir }
    & (Join-Path $root 'tools/airun.exe') publish (Join-Path $root 'src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos') --out $nativePublishDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'native bytecode .aos publish smoke failed' }
    if (-not (Test-Path (Join-Path $nativePublishDir 'app.aibc1'))) { throw 'native publish smoke failed: app.aibc1 missing' }
    if (-not (Test-Path (Join-Path $nativePublishDir 'vm_c_execute_src_main_params.exe'))) { throw 'native publish smoke failed: app executable missing' }
    if ((Test-Path (Join-Path $nativePublishDir 'run.ps1')) -or (Test-Path (Join-Path $nativePublishDir 'run.cmd')) -or (Test-Path (Join-Path $nativePublishDir 'run.sh'))) { throw 'native publish smoke failed: launcher files should not exist' }
    $hostRid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'windows-arm64' } else { 'windows-x64' }
    $projectDir = Join-Path $root '.tmp/aivm-c-native-project-target'
    $projectOut = Join-Path $root '.tmp/aivm-c-native-project-target-out'
    if (Test-Path $projectDir) { Remove-Item -Recurse -Force $projectDir }
    if (Test-Path $projectOut) { Remove-Item -Recurse -Force $projectOut }
    New-Item -ItemType Directory -Force -Path $projectDir | Out-Null
    @"
Program#p1 {
  Project#proj1(name="projtarget" entryFile="main.aos" entryExport="main" publishTarget="$hostRid")
}
"@ | Set-Content -Path (Join-Path $projectDir 'project.aiproj') -NoNewline
    @"
Bytecode#bc1(magic="AIBC" format="AiBC1" version=2 flags=0) {
  Func#f1(name=main params="argv" locals="") {
    Inst#i1(op=HALT)
  }
}
"@ | Set-Content -Path (Join-Path $projectDir 'main.aos') -NoNewline
    & (Join-Path $root 'tools/airun.exe') publish $projectDir --out $projectOut | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'native publish target-from-manifest failed' }
    if (-not (Test-Path (Join-Path $projectOut 'projtarget.exe'))) { throw 'native publish target-from-manifest failed: projtarget.exe missing' }
    Set-Content -Path $parityReport -Value 'parity manifest skipped: source-mode dualrun removed in C-only runtime cutover'
  } else {
    Set-Content -Path $parityReport -Value 'parity manifest skipped: missing ./tools/airun.exe'
  }
} else {
  if (Test-Path (Join-Path $root 'tools/airun')) {
    & (Join-Path $root 'tools/airun') run (Join-Path $root 'src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos') --vm=c | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'native bytecode .aos run smoke failed' }
    $nativePublishDir = Join-Path $root '.tmp/aivm-c-native-publish-smoke'
    if (Test-Path $nativePublishDir) { Remove-Item -Recurse -Force $nativePublishDir }
    & (Join-Path $root 'tools/airun') publish (Join-Path $root 'src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos') --out $nativePublishDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'native bytecode .aos publish smoke failed' }
    if (-not (Test-Path (Join-Path $nativePublishDir 'app.aibc1'))) { throw 'native publish smoke failed: app.aibc1 missing' }
    if (-not (Test-Path (Join-Path $nativePublishDir 'vm_c_execute_src_main_params'))) { throw 'native publish smoke failed: app executable missing' }
    if ((Test-Path (Join-Path $nativePublishDir 'run.ps1')) -or (Test-Path (Join-Path $nativePublishDir 'run.cmd')) -or (Test-Path (Join-Path $nativePublishDir 'run.sh'))) { throw 'native publish smoke failed: launcher files should not exist' }
    & (Join-Path $nativePublishDir 'vm_c_execute_src_main_params') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'native publish smoke failed: app executable did not run' }
    $hostRid = if ($IsMacOS) {
      if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) { 'osx-arm64' } else { 'osx-x64' }
    } elseif ($IsLinux) {
      if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) { 'linux-arm64' } else { 'linux-x64' }
    } else {
      ''
    }
    if ($hostRid -ne '') {
      $projectDir = Join-Path $root '.tmp/aivm-c-native-project-target'
      $projectOut = Join-Path $root '.tmp/aivm-c-native-project-target-out'
      if (Test-Path $projectDir) { Remove-Item -Recurse -Force $projectDir }
      if (Test-Path $projectOut) { Remove-Item -Recurse -Force $projectOut }
      New-Item -ItemType Directory -Force -Path $projectDir | Out-Null
      @"
Program#p1 {
  Project#proj1(name="projtarget" entryFile="main.aos" entryExport="main" publishTarget="$hostRid")
}
"@ | Set-Content -Path (Join-Path $projectDir 'project.aiproj') -NoNewline
      @"
Bytecode#bc1(magic="AIBC" format="AiBC1" version=2 flags=0) {
  Func#f1(name=main params="argv" locals="") {
    Inst#i1(op=HALT)
  }
}
"@ | Set-Content -Path (Join-Path $projectDir 'main.aos') -NoNewline
      & (Join-Path $root 'tools/airun') publish $projectDir --out $projectOut | Out-Null
      if ($LASTEXITCODE -ne 0) { throw 'native publish target-from-manifest failed' }
      if (-not (Test-Path (Join-Path $projectOut 'projtarget'))) { throw 'native publish target-from-manifest failed: projtarget missing' }
    }
    Set-Content -Path $parityReport -Value 'parity manifest skipped: source-mode dualrun removed in C-only runtime cutover'
  } else {
    Set-Content -Path $parityReport -Value 'parity manifest skipped: missing ./tools/airun'
  }
}
} else {
  Set-Content -Path $parityReport -Value 'parity manifest skipped: legacy smoke disabled (set AIVM_LEGACY_SMOKE=1 to enable)'
}

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
