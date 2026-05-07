#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source "${ROOT_DIR}/scripts/aivm-native-paths.sh"
NATIVE_DIR="$(require_aivm_native_dir "${ROOT_DIR}")"
BUILD_DIR="${AIVM_C_BUILD_DIR:-${ROOT_DIR}/../AiVM/.tmp/aivm-c-build-native}"
LOG_DIR="${ROOT_DIR}/.tmp"
LOG_FILE="${LOG_DIR}/test-wasm-ctest.log"
PRESET_FILE="${NATIVE_DIR}/CMakePresets.json"

if ! command -v emcc >/dev/null 2>&1; then
  echo "missing dependency: emcc" >&2
  exit 2
fi
if ! command -v wasmtime >/dev/null 2>&1; then
  echo "missing dependency: wasmtime" >&2
  exit 2
fi

mkdir -p "${LOG_DIR}"

if [[ -f "${PRESET_FILE}" ]]; then
  cmake --preset aivm-native-unix --fresh
  cmake --build --preset aivm-native-unix-build
  ctest --preset aivm-native-unix-test-wasm --output-on-failure | tee "${LOG_FILE}"
  wasm_tests="$(ctest --preset aivm-native-unix-test-wasm -N | awk '/Total Tests:/ {print $3}')"
else
  cmake -S "${NATIVE_DIR}" -B "${BUILD_DIR}" -DAIVM_BUILD_SHARED=OFF
  cmake --build "${BUILD_DIR}"
  ctest --test-dir "${BUILD_DIR}" -L wasm --output-on-failure | tee "${LOG_FILE}"
  wasm_tests="$(ctest --test-dir "${BUILD_DIR}" -L wasm -N | awk '/Total Tests:/ {print $3}')"
fi

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "### WASM Golden Coverage"
    echo
    echo "| Metric | Value |"
    echo "|---|---|"
    echo "| ctest wasm label tests | ${wasm_tests:-0} |"
    echo "| wasm ctest status | pass |"
  } >> "${GITHUB_STEP_SUMMARY}"
fi
