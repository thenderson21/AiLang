#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourcePath = Join-Path $root 'src/AiCLI/native/airun.c'
$nativeInclude = Join-Path $root 'src/AiVM.Core/native/include'
$nativeSrc = Join-Path $root 'src/AiVM.Core/native/src'
$hostWrapperPath = Join-Path $root 'tools/airun.exe'

$targetArch = if ($env:AIVM_AIRUN_ARCH) { $env:AIVM_AIRUN_ARCH } else { 'x64' }
if ($targetArch -ne 'x64' -and $targetArch -ne 'arm64') {
  throw "unsupported AIVM_AIRUN_ARCH: $targetArch"
}

$outDir = Join-Path $root ".artifacts/airun-windows-$targetArch"
$wrapperPath = Join-Path $outDir 'airun.exe'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Get-Command cl -ErrorAction SilentlyContinue)) {
  throw 'MSVC cl.exe is required. Use ilammy/msvc-dev-cmd in CI.'
}

$sources = @(
  $sourcePath,
  (Join-Path $nativeSrc 'aivm_types.c'),
  (Join-Path $nativeSrc 'aivm_vm.c'),
  (Join-Path $nativeSrc 'aivm_program.c'),
  (Join-Path $nativeSrc 'sys/aivm_syscall.c'),
  (Join-Path $nativeSrc 'sys/aivm_syscall_contracts.c'),
  (Join-Path $nativeSrc 'aivm_parity.c'),
  (Join-Path $nativeSrc 'aivm_runtime.c'),
  (Join-Path $nativeSrc 'aivm_c_api.c'),
  (Join-Path $nativeSrc 'remote/aivm_remote_channel.c'),
  (Join-Path $nativeSrc 'remote/aivm_remote_session.c')
)

$clArgs = @('/nologo', '/O2', '/W4', '/WX', '/std:c11', '/D_CRT_SECURE_NO_WARNINGS', "/I$nativeInclude", "/Fe:$wrapperPath") + $sources + @('Ws2_32.lib')
& cl @clArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem -Path $root -Filter '*.obj' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

if ($targetArch -eq 'x64') {
  Copy-Item $wrapperPath $hostWrapperPath -Force
}
