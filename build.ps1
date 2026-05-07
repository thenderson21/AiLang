$ErrorActionPreference = 'Stop'

function Show-Usage {
  @'
Usage: ./build.ps1 [host|shared|wasm|all]

Builds AiLang tooling through the selected installed SDK.

Targets:
  host    Stage host tools from the selected installed SDK (default).
  shared  Delegated to AiVM; kept temporarily for migration compatibility.
  wasm    Delegated to AiVM; kept temporarily for migration compatibility.
  all     Stage host tools and run delegated compatibility targets.
'@
}

function Get-ProjectToolchain {
  $dir = $PSScriptRoot
  while ($true) {
    $toml = Join-Path $dir 'ailang-toolchain.toml'
    if (Test-Path $toml) {
      $match = Select-String -Path $toml -Pattern '^\s*version\s*=\s*"([^"]+)"' | Select-Object -First 1
      if ($match) { return $match.Matches[0].Groups[1].Value }
    }
    $legacy = Join-Path $dir '.ailang-toolchain'
    if (Test-Path $legacy) {
      return ((Get-Content $legacy -TotalCount 1) -as [string]).Trim()
    }
    $parent = Split-Path $dir -Parent
    if ($parent -eq $dir) { return '' }
    $dir = $parent
  }
}

function Invoke-StageInstalledToolchain {
  $installRoot = if ($env:AILANG_INSTALL_ROOT) { $env:AILANG_INSTALL_ROOT } else { Join-Path $HOME '.ailang' }
  $selected = if ($env:AILANG_TOOLCHAIN) { $env:AILANG_TOOLCHAIN } else { Get-ProjectToolchain }
  if ($selected) {
    if ($selected -eq 'local') {
      $sdkRoot = Join-Path $installRoot 'local'
    } else {
      $sdkRoot = Join-Path (Join-Path $installRoot 'toolchains') $selected
    }
  } else {
    $sdkRoot = Join-Path $installRoot 'current'
  }
  $sdkBin = Join-Path $sdkRoot 'bin'
  $toolsDir = Join-Path $PSScriptRoot 'tools'
  $ailang = Join-Path $sdkBin 'ailang.exe'
  if (-not (Test-Path $ailang)) {
    $ailang = Join-Path $sdkBin 'ailang'
  }
  if (-not (Test-Path $ailang)) {
    throw "selected AiLang SDK is missing bin/ailang: $sdkRoot"
  }
  New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
  Copy-Item $ailang (Join-Path $toolsDir 'ailang.exe') -Force
  $airun = Join-Path $sdkBin 'airun.exe'
  if (-not (Test-Path $airun)) { $airun = Join-Path $sdkBin 'airun' }
  if (Test-Path $airun) {
    Copy-Item $airun (Join-Path $toolsDir 'airun.exe') -Force
  } else {
    Copy-Item $ailang (Join-Path $toolsDir 'airun.exe') -Force
  }
  $runtime = Join-Path $sdkBin 'aivm-runtime.exe'
  if (-not (Test-Path $runtime)) { $runtime = Join-Path $sdkBin 'aivm-runtime' }
  if (Test-Path $runtime) {
    Copy-Item $runtime (Join-Path $toolsDir 'aivm-runtime.exe') -Force
  }
  $frontend = Join-Path $sdkBin 'aos_frontend.exe'
  if (-not (Test-Path $frontend)) { $frontend = Join-Path $sdkBin 'aos_frontend' }
  if (Test-Path $frontend) {
    Copy-Item $frontend (Join-Path $toolsDir 'aos_frontend.exe') -Force
  }
  Write-Host "Staged AiLang tools from installed SDK: $sdkRoot"
}

function Invoke-BuildTarget([string]$Target) {
  switch ($Target) {
    'host' {
      Invoke-StageInstalledToolchain
    }
    'shared' {
      bash "$PSScriptRoot/scripts/build-aivm-c-shared.sh"
      if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    'wasm' {
      bash "$PSScriptRoot/scripts/build-aivm-wasm.sh"
      if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    'all' {
      Invoke-BuildTarget 'host'
      Invoke-BuildTarget 'shared'
      Invoke-BuildTarget 'wasm'
    }
    'help' { Show-Usage }
    '--help' { Show-Usage }
    '-h' { Show-Usage }
    default {
      Write-Error "unknown build target: $Target"
      Show-Usage | Write-Host
      exit 1
    }
  }
}

$target = if ($args.Length -gt 0) { $args[0] } else { 'host' }
Invoke-BuildTarget $target
