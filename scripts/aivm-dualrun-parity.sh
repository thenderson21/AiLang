#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-dualrun"
LEFT_OUT="${TMP_DIR}/left.out"
RIGHT_OUT="${TMP_DIR}/right.out"
PARITY_SHELL="${AIVM_PARITY_SHELL:-bash}"

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <left-command> <right-command>" >&2
  exit 2
fi

LEFT_CMD="$1"
RIGHT_CMD="$2"

mkdir -p "${TMP_DIR}"

"${PARITY_SHELL}" -lc "$LEFT_CMD" >"${LEFT_OUT}" 2>&1
"${PARITY_SHELL}" -lc "$RIGHT_CMD" >"${RIGHT_OUT}" 2>&1

"${ROOT_DIR}/scripts/aivm-parity-compare.sh" "${LEFT_OUT}" "${RIGHT_OUT}"
