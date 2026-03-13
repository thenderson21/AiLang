#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

OUT="$(./tools/airun debug disasm ./samples/cli-fetch/src/app.aibc1 0 5)"
printf '%s\n' "$OUT" | grep -q $'^0\t'
printf '%s\n' "$OUT" | grep -q 'CONST'
