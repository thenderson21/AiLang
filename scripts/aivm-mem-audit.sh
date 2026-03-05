#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <program-path-or-dir> [iterations]" >&2
  exit 2
fi

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="$1"
ITERATIONS="${2:-20}"
VM_MODE="${AIVM_MEM_AUDIT_VM_MODE:---vm=c}"
REPORT="${AIVM_MEM_AUDIT_REPORT:-${ROOT_DIR}/.tmp/aivm-mem-audit.toml}"
MAX_GROWTH_KB="${AIVM_LEAK_MAX_RSS_GROWTH_KB:-2048}"

if ! [[ "${ITERATIONS}" =~ ^[0-9]+$ ]] || [[ "${ITERATIONS}" -le 0 ]]; then
  echo "iterations must be a positive integer" >&2
  exit 2
fi

if ! [[ "${MAX_GROWTH_KB}" =~ ^-?[0-9]+$ ]]; then
  echo "AIVM_LEAK_MAX_RSS_GROWTH_KB must be an integer" >&2
  exit 2
fi

if [[ ! -x "${ROOT_DIR}/tools/airun" ]]; then
  echo "missing runtime: ${ROOT_DIR}/tools/airun" >&2
  exit 2
fi

mkdir -p "$(dirname "${REPORT}")"
cd "${ROOT_DIR}"
./tools/airun debug profile "${TARGET}" --iterations "${ITERATIONS}" --max-growth-kb "${MAX_GROWTH_KB}" --out "${REPORT}" "${VM_MODE}"
