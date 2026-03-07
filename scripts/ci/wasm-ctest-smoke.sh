#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NATIVE_DIR="${ROOT_DIR}/src/AiVM.Core/native"
LOG_DIR="${ROOT_DIR}/.tmp"
LOG_FILE="${LOG_DIR}/test-wasm-ctest.log"

if ! command -v emcc >/dev/null 2>&1; then
  echo "missing dependency: emcc" >&2
  exit 2
fi
if ! command -v wasmtime >/dev/null 2>&1; then
  echo "missing dependency: wasmtime" >&2
  exit 2
fi

mkdir -p "${LOG_DIR}"

pushd "${NATIVE_DIR}" >/dev/null
cmake --preset aivm-native-unix --fresh
cmake --build --preset aivm-native-unix-build
ctest --preset aivm-native-unix-test-wasm --output-on-failure | tee "${LOG_FILE}"
wasm_tests="$(ctest --preset aivm-native-unix-test-wasm -N | awk '/Total Tests:/ {print $3}')"
popd >/dev/null

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
