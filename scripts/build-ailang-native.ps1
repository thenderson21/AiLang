#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$nativeSrc = if ($env:AIVM_C_SOURCE_DIR) {
  $env:AIVM_C_SOURCE_DIR
} elseif (Test-Path (Join-Path $root '..\AiVM\native')) {
  (Resolve-Path (Join-Path $root '..\AiVM\native')).Path
} else {
  throw 'AiVM native source not found. Set AIVM_C_SOURCE_DIR or place AiVM next to AiLang.'
}

$sourcePath = Join-Path $nativeSrc 'ailang_cli\ailang.c'
$uiHostWindowsPath = Join-Path $nativeSrc 'ailang_cli\airun_ui_host_windows.c'
$nativeInclude = Join-Path $nativeSrc 'include'
$hostWrapperPath = Join-Path $root 'tools\ailang.exe'
$hostRuntimePath = Join-Path $root 'tools\aivm-runtime.exe'

$targetArch = if ($env:AILANG_NATIVE_ARCH) { $env:AILANG_NATIVE_ARCH } else { 'x64' }
if ($targetArch -ne 'x64' -and $targetArch -ne 'arm64') {
  throw "unsupported AILANG_NATIVE_ARCH: $targetArch"
}

$outDir = Join-Path $root ".artifacts\ailang-windows-$targetArch"
$wrapperPath = Join-Path $outDir 'ailang.exe'
$runtimePath = Join-Path $outDir 'aivm-runtime.exe'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Get-Command cl -ErrorAction SilentlyContinue)) {
  throw 'MSVC cl.exe is required. Use ilammy/msvc-dev-cmd in CI.'
}

$sources = @(
  $sourcePath,
  $uiHostWindowsPath,
  (Join-Path $nativeSrc 'ailang_native_bridge.c'),
  (Join-Path $nativeSrc 'ailang_package_manager.c'),
  (Join-Path $nativeSrc 'aivm_types.c'),
  (Join-Path $nativeSrc 'aivm_vm.c'),
  (Join-Path $nativeSrc 'aivm_program.c'),
  (Join-Path $nativeSrc 'sys\aivm_syscall.c'),
  (Join-Path $nativeSrc 'sys\aivm_syscall_contracts.c'),
  (Join-Path $nativeSrc 'aivm_parity.c'),
  (Join-Path $nativeSrc 'aivm_runtime.c'),
  (Join-Path $nativeSrc 'aivm_c_api.c'),
  (Join-Path $nativeSrc 'remote\aivm_remote_channel.c'),
  (Join-Path $nativeSrc 'remote\aivm_remote_session.c'),
  (Join-Path $nativeSrc 'remote\aivm_remote_transport.c'),
  (Join-Path $nativeSrc 'remote\aivm_remote_ws_frame.c')
)
$commonArgs = @(
  '/nologo',
  '/O2',
  '/W4',
  '/WX',
  '/std:c11',
  '/D_CRT_SECURE_NO_WARNINGS',
  '/DAIRUN_UI_HOST_EXTERNAL=1',
  "/I$nativeInclude",
  "/I$(Join-Path $nativeSrc 'ailang_cli')"
)
$linkLibs = @('Ws2_32.lib', 'psapi.lib', 'user32.lib', 'gdi32.lib', 'Shell32.lib', 'Ole32.lib', 'Windowscodecs.lib', 'Uuid.lib')

$clArgs = $commonArgs + @("/Fe:$wrapperPath") + $sources + $linkLibs
& cl @clArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runtimeArgs = $commonArgs + @('/DAIRUN_MINIMAL_RUNTIME=1', "/Fe:$runtimePath") + $sources + $linkLibs
& cl @runtimeArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem -Path $root -Filter '*.obj' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

if ($targetArch -eq 'x64') {
  New-Item -ItemType Directory -Force -Path (Join-Path $root 'tools') | Out-Null
  Copy-Item $wrapperPath $hostWrapperPath -Force
  Copy-Item $runtimePath $hostRuntimePath -Force
  Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $root 'tools\ailang.exe'), (Join-Path $root 'tools\ailang')
}
