$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest = Join-Path $root 'Docs\SDK-Docs-Manifest.tsv'

if ($args.Count -ne 1) {
  throw 'usage: stage-sdk-docs.ps1 <dest-root>'
}

$destRoot = $args[0]

if (-not (Test-Path $manifest)) {
  throw "missing sdk docs manifest: $manifest"
}

$stagedCount = 0

Get-Content $manifest | ForEach-Object {
  $line = $_
  if ([string]::IsNullOrWhiteSpace($line)) { return }
  if ($line.StartsWith('#')) { return }

  $parts = $line -split "`t"
  if ($parts.Count -ne 2) {
    throw "invalid sdk docs manifest row: $line"
  }

  $sourcePath = $parts[0]
  $targetPath = $parts[1]
  $sourceFile = Join-Path $root $sourcePath
  $targetFile = Join-Path $destRoot $targetPath
  $targetDir = Split-Path -Parent $targetFile

  if (-not (Test-Path $sourceFile)) {
    throw "sdk docs manifest missing file: $sourcePath"
  }

  New-Item -ItemType Directory -Force $targetDir | Out-Null
  Copy-Item $sourceFile $targetFile -Force
  $stagedCount += 1
}

if ($stagedCount -eq 0) {
  throw "sdk docs manifest is empty: $manifest"
}

Write-Host "sdk docs staged: $stagedCount files -> $destRoot"
