#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

TMP_DIR="${ROOT}/.tmp/test-airun-debug-disasm"
rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

./tools/airun build ./samples/cli-fetch --out "${TMP_DIR}" >/dev/null
OUT="$(./tools/airun debug disasm "${TMP_DIR}/app.aibc1" 0 5)"
printf '%s\n' "$OUT" | grep -q $'^0\t'
printf '%s\n' "$OUT" | grep -q 'CONST'
