#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${ROOT_DIR}/.tmp/aivm-c-build"

cmake -S "${ROOT_DIR}/AiVM.C" -B "${BUILD_DIR}"
cmake --build "${BUILD_DIR}"
ctest --test-dir "${BUILD_DIR}" --output-on-failure
"${ROOT_DIR}/scripts/aivm-dualrun-parity-manifest.sh" "${ROOT_DIR}/AiVM.C/tests/parity_commands.txt" "${ROOT_DIR}/.tmp/aivm-dualrun-manifest/report.txt"

if [[ "${AIVM_PERF_SMOKE:-0}" == "1" ]]; then
  "${ROOT_DIR}/scripts/aivm-c-perf-smoke.sh" "${AIVM_PERF_RUNS:-10}"
fi
