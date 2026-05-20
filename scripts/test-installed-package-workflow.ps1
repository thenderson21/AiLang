$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$tmpRoot = if ($env:AILANG_PACKAGE_SMOKE_ROOT) {
  $env:AILANG_PACKAGE_SMOKE_ROOT
} elseif ($env:RUNNER_TEMP) {
  Join-Path $env:RUNNER_TEMP ([System.Guid]::NewGuid().ToString('N'))
} else {
  Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N'))
}

$appDir = Join-Path $tmpRoot 'package-app'
$templateDir = Join-Path $tmpRoot 'template-app'

function Require-Tool {
  param([string]$Name)
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "missing required tool: $Name"
  }
}

function Write-Utf8File {
  param(
    [string]$Path,
    [string]$Content
  )
  $parent = Split-Path -Parent $Path
  New-Item -ItemType Directory -Force $parent | Out-Null
  [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

Require-Tool ailang
Require-Tool git

Remove-Item -Recurse -Force $appDir, $templateDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $appDir 'src'), (Join-Path $templateDir 'src') | Out-Null

Write-Utf8File (Join-Path $appDir 'project.aiproj') @'
Program#p1 {
  Project#proj1(name="package-smoke" entryFile="src/app.aos" entryExport="start" version="0.0.1-beta.1") {
    Include#dep_ailang(name="ailang")
    Include#dep_json(name="std-json")
  }
}
'@

Write-Utf8File (Join-Path $appDir 'src/app.aos') @'
Program#p1 {
  Import#i1(package="ailang" path="src/std/core.aos")
  Import#i2(package="std-json" path="src/json.aos")
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Let#l2(name=value) {
          Call#c1(target=resultValueOr) {
            Call#c2(target=parse) { Lit#s1(value="\"package-smoke\"") }
            Lit#s2(value="fallback")
          }
        }
        Call#c3(target=sys.stdout.writeLine) { StrConcat#sc1 { Lit#s3(value="Package smoke: ") Var#v1(name=value) } }
        Return#r1 { Lit#i1(value=0) }
      }
    }
  }
}
'@

ailang package restore $appDir
$packageList = (ailang package list $appDir) -join "`n"
if ($packageList -notmatch 'std-json') { throw 'std-json missing from package list' }
ailang build $appDir
$packageRunOut = Join-Path $tmpRoot 'package-run.stdout.txt'
$packageRunErr = Join-Path $tmpRoot 'package-run.stderr.txt'
ailang run $appDir 1>$packageRunOut 2>$packageRunErr
$packageRun = Get-Content -Raw -ErrorAction SilentlyContinue $packageRunOut
$packageRunErrText = Get-Content -Raw -ErrorAction SilentlyContinue $packageRunErr
if ($packageRun -notmatch 'Package smoke:') {
  throw "package app output mismatch: stdout=$packageRun stderr=$packageRunErrText"
}

Write-Utf8File (Join-Path $templateDir 'project.aiproj') @'
Program#p1 {
  Project#proj1(name="template-smoke" entryFile="src/app.aos" entryExport="start" version="0.0.1-beta.1") {
    Include#dep_aivectra(name="aivectra")
  }
}
'@

Write-Utf8File (Join-Path $templateDir 'src/app.aos') @'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) { Fn#f1(params=args) { Block#b1 { Return#r1 { Lit#i1(value=0) } } } }
}
'@

ailang package restore $templateDir
$templatePackageList = (ailang package list $templateDir) -join "`n"
if ($templatePackageList -notmatch 'aivectra') { throw 'aivectra missing from package list' }
$projectTemplates = (ailang template list projects $templateDir) -join "`n"
if ($projectTemplates -notmatch 'aivectra/hello-name') { throw 'aivectra project template missing' }
$fileTemplates = (ailang template list files $templateDir) -join "`n"
if ($fileTemplates -notmatch 'aivectra/view-basic') { throw 'aivectra file template missing' }

$toolOut = Join-Path $tmpRoot 'aivectra-tool.stdout.txt'
$toolErr = Join-Path $tmpRoot 'aivectra-tool.stderr.txt'
$oldTimeout = $env:AILANG_PACKAGE_TOOL_TIMEOUT_SECONDS
$oldNativePref = $PSNativeCommandUseErrorActionPreference
if (-not $env:AILANG_PACKAGE_TOOL_TIMEOUT_SECONDS) {
  $env:AILANG_PACKAGE_TOOL_TIMEOUT_SECONDS = '10'
}
$PSNativeCommandUseErrorActionPreference = $false
try {
  Push-Location $templateDir
  try {
    ailang aivectra help 1>$toolOut 2>$toolErr
  } finally {
    Pop-Location
  }
  $toolStatus = $LASTEXITCODE
} finally {
  $PSNativeCommandUseErrorActionPreference = $oldNativePref
  $env:AILANG_PACKAGE_TOOL_TIMEOUT_SECONDS = $oldTimeout
}
if ($toolStatus -ne 0) {
  $toolErrText = Get-Content -Raw -ErrorAction SilentlyContinue $toolErr
  if ($toolErrText -notmatch 'package tool timed out') {
    $toolOutText = Get-Content -Raw -ErrorAction SilentlyContinue $toolOut
    throw "aivectra package tool failed unexpectedly: stdout=$toolOutText stderr=$toolErrText"
  }
}

if (-not $env:AILANG_PACKAGE_SMOKE_KEEP) {
  Remove-Item -Recurse -Force $tmpRoot -ErrorAction SilentlyContinue
}

Write-Output 'package workflow smoke passed'
