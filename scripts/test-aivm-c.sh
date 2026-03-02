#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFERRED_C_SOURCE_DIR="${ROOT_DIR}/src/AiVM.Core/native"
AIVM_C_SOURCE_DIR="${AIVM_C_SOURCE_DIR:-${PREFERRED_C_SOURCE_DIR}}"
BUILD_SUFFIX="native"
BUILD_DIR="${AIVM_C_BUILD_DIR:-${ROOT_DIR}/.tmp/aivm-c-build-${BUILD_SUFFIX}}"
PARITY_REPORT="${AIVM_PARITY_REPORT:-${ROOT_DIR}/.tmp/aivm-dualrun-manifest/report.txt}"
PARITY_MANIFEST="${AIVM_PARITY_MANIFEST:-${AIVM_C_SOURCE_DIR}/tests/parity_commands_ci.txt}"
SHARED_FLAG="-DAIVM_BUILD_SHARED=OFF"
if [[ "${AIVM_BUILD_SHARED:-0}" == "1" ]]; then
  SHARED_FLAG="-DAIVM_BUILD_SHARED=ON"
fi

cmake -S "${AIVM_C_SOURCE_DIR}" -B "${BUILD_DIR}" "${SHARED_FLAG}"
cmake --build "${BUILD_DIR}"
ctest --test-dir "${BUILD_DIR}" --output-on-failure
mkdir -p "$(dirname "${PARITY_REPORT}")"
"${ROOT_DIR}/scripts/aivm-dualrun-parity-manifest.sh" "${PARITY_MANIFEST}" "${PARITY_REPORT}"

if [[ "${AIVM_PERF_SMOKE:-0}" == "1" ]]; then
  "${ROOT_DIR}/scripts/aivm-c-perf-smoke.sh" "${AIVM_PERF_RUNS:-10}"
fi
