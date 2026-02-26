#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${ROOT_DIR}/.tmp/aivm-c-build"

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <left-output-file> <right-output-file>" >&2
  exit 2
fi

LEFT="$1"
RIGHT="$2"

if [[ ! -f "$LEFT" ]]; then
  echo "missing left file: $LEFT" >&2
  exit 2
fi

if [[ ! -f "$RIGHT" ]]; then
  echo "missing right file: $RIGHT" >&2
  exit 2
fi

cmake -S "${ROOT_DIR}/AiVM.C" -B "${BUILD_DIR}" >/dev/null
cmake --build "${BUILD_DIR}" --target aivm_parity_cli >/dev/null

"${BUILD_DIR}/aivm_parity_cli" "$LEFT" "$RIGHT"
